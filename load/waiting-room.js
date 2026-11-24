import http from 'k6/http';
import { check, fail, sleep } from 'k6';
import exec from 'k6/execution';
import { SharedArray } from 'k6/data';
import { Counter, Rate, Trend } from 'k6/metrics';

const tokens = new SharedArray('waiting-room-tokens', () => [JSON.parse(open('/state/tokens.json'))])[0];
const base = __ENV.BASE_URL || 'http://nginx:8080';
const mode = __ENV.WAITING_ROOM_MODE || 'Active';
const arrivals = Number(__ENV.WAITING_ROOM_ARRIVALS || 100);
const windowSeconds = Number(__ENV.WAITING_ROOM_WINDOW_SECONDS || 1);
const rate = Number(__ENV.WAITING_ROOM_RATE || 5);
const runId = __ENV.RUN_ID || 'local';
const allocatedVUs = windowSeconds === 1 ? Math.ceil(arrivals * 1.2) + 10
  : Math.min(100, Math.max(30, Math.ceil(arrivals / windowSeconds * 30) + 20));
const session = '80000000-0000-0000-0000-000000000002';
const ticketType = '80000000-0000-0000-0000-000000000003';
if (!['Active', 'Disabled'].includes(mode) || !Number.isInteger(arrivals) || arrivals < 1 || arrivals > 1000
    || !Number.isInteger(windowSeconds) || windowSeconds < 1 || windowSeconds > 3600
    || !Number.isInteger(rate) || rate < 1 || rate > 1000) throw new Error('Invalid waiting-room manifest');

const technical = new Rate('wr_technical_errors');
const joinToDecision = new Trend('wr_join_to_terminal_ms', true);
const grantWait = new Trend('wr_grant_wait_ms', true);
const paidToTicket = new Trend('wr_paid_to_ticket_ms', true);
const joinToTicket = new Trend('wr_join_to_ticket_ms', true);
const statusLatency = new Trend('wr_status_latency_ms', true);
const holdLatency = new Trend('wr_hold_latency_ms', true);
const joins = new Counter('wr_joins');
const admitted = new Counter('wr_admitted');
const rejected = new Counter('wr_rejected');
const expired = new Counter('wr_expired');
const undecided = new Counter('wr_undecided');
const holds = new Counter('wr_holds_created');
const soldOut = new Counter('wr_capacity_conflicts');
const paid = new Counter('wr_paid_orders');
const tickets = new Counter('wr_tickets_issued');
const replayFailures = new Counter('wr_replay_failures');

export const options = {
  scenarios: {
    arrivals: { executor: 'constant-arrival-rate', exec: 'buy', rate: arrivals,
      timeUnit: `${windowSeconds}s`, duration: `${windowSeconds}s`, preAllocatedVUs: allocatedVUs, maxVUs: allocatedVUs,
      gracefulStop: '90s' },
  },
  setupTimeout: '120s',
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(95)', 'p(99)'],
  thresholds: { wr_technical_errors: ['rate<0.01'], checks: ['rate==1'],
    dropped_iterations: ['count==0'] },
  systemTags: ['status', 'method', 'name', 'scenario', 'expected_response', 'check'],
};
http.setResponseCallback(http.expectedStatuses(200, 201, 204, 409, 429, 503));

function json(response) { try { return response.json(); } catch (_) { return {}; } }
function request(token, name, key, version, admissionToken) {
  return { timeout: '30s', tags: { name }, headers: {
    Authorization: `Bearer ${token}`, 'Content-Type': 'application/json',
    ...(key ? { 'Idempotency-Key': key } : {}),
    ...(version !== undefined ? { 'X-Expected-Version': String(version) } : {}),
    ...(admissionToken ? { 'X-Admission-Token': admissionToken } : {}),
  } };
}
function observe(response, name, allowed) {
  const body = json(response);
  const ok = allowed.includes(response.status);
  technical.add(!ok, { endpoint: name });
  check(response, { [`${name} expected status`]: () => ok });
  return body;
}

export function setup() {
  let ready = false;
  for (let attempt = 0; attempt < 60; attempt++) {
    if (http.get(`${base}/health`, { tags: { name: 'setup-health' } }).status === 200) { ready = true; break; }
    sleep(1);
  }
  if (!ready) fail('API health unavailable');
  const me = http.get(`${base}/api/v1/users/me`, request(tokens.customers[0], 'setup-user'));
  if (me.status !== 200) fail(`Load identity unavailable: ${me.status}`);
  if (mode === 'Active') {
    const probe = http.post(`${base}/api/v1/holds`, JSON.stringify({ eventSessionId: session,
      items: [{ ticketTypeId: ticketType, inventoryMode: 'GeneralAdmission', quantity: 1 }] }),
    request(tokens.customers[9999], 'setup-gate', `${runId}-bypass-probe`));
    if (probe.status !== 409 || json(probe).code !== 'WAITING_ROOM_REQUIRED')
      fail(`Hold Gate bypass probe failed: ${probe.status} ${json(probe).code || ''}`);
  }
}

export function buy() {
  const index = exec.scenario.iterationInTest;
  // k6 can schedule one boundary iteration at exactly 1s. Keep HTTP arrivals exact.
  if (index >= arrivals) return;
  const token = tokens.customers[index];
  const started = Date.now();
  const join = http.post(`${base}/api/v1/waiting-room/join`, JSON.stringify({ eventSessionId: session }),
    request(token, 'wr_join'));
  const joined = observe(join, 'wr_join', [200, 409]);
  if (join.status === 409) {
    const known = joined.code === 'SALES_WINDOW_CLOSED' || joined.code === 'WAITING_ROOM_CLOSED';
    check(join, { 'join rejection has sale code': () => known });
    if (known) rejected.add(1); else technical.add(true, { endpoint: 'wr_join_code' });
    joinToDecision.add(Date.now() - started);
    return;
  }
  joins.add(1);
  const data = joined.data || {};
  check(join, { 'join mode matches policy': () => data.mode === (mode === 'Active' ? 'Waiting' : 'Direct') });
  if (data.mode !== (mode === 'Active' ? 'Waiting' : 'Direct')) { technical.add(true); return; }
  let admissionToken;
  if (mode === 'Active') {
    if (!data.queueId) { technical.add(true, { endpoint: 'wr_queue_id' }); return; }
    // Match the checkout client: the first poll also follows the server interval.
    sleep(Math.min(5, Math.max(3, Number(data.pollAfterMs || 3000) / 1000)) + Math.random() * 0.5);
    let decision = false;
    for (let attempt = 0; attempt < 20; attempt++) {
      const status = http.get(`${base}/api/v1/waiting-room/status?queueId=${data.queueId}`,
        request(token, 'wr_status'));
      statusLatency.add(status.timings.duration);
      const state = observe(status, 'wr_status', [200]);
      if (status.status !== 200) return;
      const current = state.data || {};
      if (current.status === 'Admitted' && current.admissionToken) {
        admissionToken = current.admissionToken;
        admitted.add(1);
        grantWait.add(Date.now() - started);
        decision = true;
        break;
      }
      if (current.status === 'Rejected') { rejected.add(1); joinToDecision.add(Date.now() - started); return; }
      if (current.status === 'Expired') { expired.add(1); joinToDecision.add(Date.now() - started); return; }
      check(status, { 'status waits or admits': () => current.status === 'Waiting' });
      sleep(Math.min(5, Math.max(3, Number(current.pollAfterMs || 3000) / 1000)) + Math.random() * 0.5);
    }
    if (!decision) { undecided.add(1); joinToDecision.add(Date.now() - started); return; }
  }

  const key = `${runId}-customer-${index}-hold`;
  const payload = JSON.stringify({ eventSessionId: session,
    items: [{ ticketTypeId: ticketType, inventoryMode: 'GeneralAdmission', quantity: 1 }] });
  const holdParams = request(token, 'wr_hold', key, undefined, admissionToken);
  const hold = http.post(`${base}/api/v1/holds`, payload, holdParams);
  holdLatency.add(hold.timings.duration);
  const result = observe(hold, 'wr_hold', [201, 409]);
  joinToDecision.add(Date.now() - started);
  if (hold.status === 409) {
    const capacity = result.code === 'INSUFFICIENT_CAPACITY' || result.code === 'SEAT_UNAVAILABLE';
    check(hold, { 'hold conflict is inventory only': () => capacity });
    if (capacity) soldOut.add(1); else technical.add(true, { endpoint: 'wr_hold_code' });
    return;
  }
  holds.add(1);
  const h = result.data;
  const replay = http.post(`${base}/api/v1/holds`, payload, holdParams);
  const replayData = observe(replay, 'wr_hold_replay', [201]);
  if (!replayData.data || replayData.data.id !== h.id || replay.headers['Idempotency-Replayed'] !== 'true') {
    replayFailures.add(1);
    check(replay, { 'same intent replays exactly one Hold': () => false });
  }

  const checkoutKey = `${runId}-customer-${index}-checkout`;
  const checkout = http.post(`${base}/api/v1/holds/${h.id}/checkout`, '{}',
    request(token, 'wr_checkout', checkoutKey, h.version));
  const order = observe(checkout, 'wr_checkout', [200, 201]).data;
  if (!order) return;
  const payment = http.post(`${base}/api/v1/orders/${order.id}/payments/mock`,
    JSON.stringify({ outcome: 'Succeeded' }),
    request(token, 'wr_payment', `${runId}-customer-${index}-payment`, order.version));
  const p = observe(payment, 'wr_payment', [200, 201]).data;
  if (!p) return;
  const ref = p.id.replace(/-/g, '');
  const callback = http.post(`${base}/api/v1/payment-webhooks/mock`, JSON.stringify({
    providerEventId: `wr-event-${ref}`, paymentId: p.id, providerTransactionId: `wr-txn-${ref}`,
    outcome: 'Succeeded', amount: p.amount, currency: p.currency, occurredAtUtc: new Date().toISOString(),
  }), request(tokens.callback, 'wr_callback'));
  observe(callback, 'wr_callback', [200]);
  let settled = false;
  for (let attempt = 0; attempt < 20; attempt++) {
    const paymentState = http.get(`${base}/api/v1/payments/${p.id}`, request(token, 'wr_payment_status'));
    const state = observe(paymentState, 'wr_payment_status', [200]).data;
    if (state && state.status === 'Succeeded') { settled = true; break; }
    sleep(0.5);
  }
  if (!settled) { technical.add(true, { endpoint: 'wr_payment_settlement' }); return; }
  paid.add(1);
  const paidAt = Date.now();
  for (let attempt = 0; attempt < 40; attempt++) {
    const ticketList = http.get(`${base}/api/v1/tickets?orderId=${order.id}`, request(token, 'wr_tickets'));
    const owned = observe(ticketList, 'wr_tickets', [200]).data;
    if (Array.isArray(owned) && owned.length === 1) {
      tickets.add(1);
      paidToTicket.add(Date.now() - paidAt);
      joinToTicket.add(Date.now() - started);
      return;
    }
    sleep(0.5);
  }
  technical.add(true, { endpoint: 'wr_ticket_delivery' });
}

export function handleSummary(data) {
  return { '/reports/summary.json': JSON.stringify(data, null, 2) };
}
