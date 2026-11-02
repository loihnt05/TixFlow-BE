import http from 'k6/http';
import { check, sleep, fail } from 'k6';
import exec from 'k6/execution';
import { SharedArray } from 'k6/data';
import { Rate, Trend, Counter } from 'k6/metrics';

const customers = new SharedArray('customers', () => JSON.parse(open('/state/tokens.json')).customers);
const callbackToken = new SharedArray('callback', () => [JSON.parse(open('/state/tokens.json')).callback])[0];
const base = __ENV.BASE_URL || 'http://nginx:8080';
const profile = __ENV.PROFILE || 'smoke';
const vus = Number(__ENV.VUS || 10);
const iterations = Number(__ENV.ITERATIONS || 100);
if (!['smoke', 'ramp', 'spike', 'soak'].includes(profile)) throw new Error('Unknown PROFILE');
if (!Number.isInteger(vus) || vus < 1 || vus > 10000 || !Number.isInteger(iterations) || iterations < 51 || iterations > 10000)
  throw new Error('VUS must be 1..10000; ITERATIONS must be 51..10000');
const session = '80000000-0000-0000-0000-000000000002';
const event = '80000000-0000-0000-0000-000000000001';
const quantityType = '80000000-0000-0000-0000-000000000003';
const seatType = '80000000-0000-0000-0000-000000000005';
const checkoutType = '80000000-0000-0000-0000-000000000007';
const seat = '80000000-0000-0000-0000-000000000006';
const technical = new Rate('technical_errors');
const business = new Rate('business_conflicts');
const admission = new Rate('admission_rejections');
const cache = new Rate('catalog_cache_hits');
const latency = new Trend('endpoint_latency', true);
const created = new Counter('holds_created');
const paid = new Counter('payments_succeeded');
const failed = new Counter('payments_failed');
const measuredRequests = new Counter('measured_requests');
let warming = false;
let warmIteration = 0;
function iteration() { return warming ? warmIteration : exec.scenario.iterationInTest; }
function verify(value, assertions) {
  if (!warming) return check(value, assertions);
  for (const name of Object.keys(assertions)) if (!assertions[name](value)) fail(`Warm-up: ${name}`);
  return true;
}

const scenarios = {};
if (profile === 'smoke') {
  for (const name of ['browse', 'availability', 'quantity', 'hotseat', 'checkout']) {
    scenarios[name] = { executor: 'shared-iterations', exec: name, vus: Math.min(vus, name === 'checkout' ? 10 : vus),
      iterations: name === 'checkout' ? 20 : iterations, maxDuration: '4m' };
  }
} else {
  const stages = profile === 'ramp' ? [{ duration: '30s', target: vus }, { duration: '2m', target: vus }, { duration: '30s', target: 0 }]
    : profile === 'spike' ? [{ duration: '10s', target: 1 }, { duration: '1s', target: vus }, { duration: '30s', target: vus }, { duration: '10s', target: 0 }]
    : [{ duration: '30s', target: vus }, { duration: '30m', target: vus }, { duration: '30s', target: 0 }];
  scenarios.mixed = { executor: 'ramping-vus', exec: 'mixed', startVUs: 1, stages, gracefulRampDown: '30s' };
}
export const options = {
  scenarios, setupTimeout: '120s', summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(95)', 'p(99)'],
  // 409 is accepted only after validating its business code below.
  thresholds: { technical_errors: ['rate<0.01'], checks: ['rate==1'], admission_rejections: ['rate==0'],
    ...(profile === 'smoke' ? { 'holds_created{kind:quantity}': ['count==50'], 'holds_created{kind:hotseat}': ['count==1'],
      payments_succeeded: ['count==10'], payments_failed: ['count==10'] } : {}) },
  systemTags: ['status', 'method', 'name', 'scenario', 'expected_response', 'check'],
};
http.setResponseCallback(http.expectedStatuses(200, 201, 409, 429));
function body(r) { try { return r.json(); } catch (_) { return {}; } }
function observe(r, endpoint, conflicts = []) {
  const data = body(r);
  const conflict = r.status === 409 && conflicts.includes(data.code);
  const limited = r.status === 429;
  const ok = r.status === 200 || r.status === 201 || conflict || limited;
  if (warming) {
    if (!ok || conflict || limited) fail(`Warm-up ${endpoint} failed: ${r.status} ${data.code || ''}`);
    return data.data;
  }
  technical.add(!ok, { endpoint }); business.add(conflict, { endpoint }); admission.add(limited, { endpoint });
  measuredRequests.add(1, { endpoint });
  latency.add(r.timings.duration, { endpoint });
  check(r, { 'expected HTTP contract': () => ok, '429 has Retry-After': () => !limited || !!r.headers['Retry-After'] });
  return data.data;
}
function params(endpoint, key, version, token) {
  const i = warming ? 9999 : iteration() % customers.length;
  return { timeout: '60s', tags: { name: endpoint }, headers: {
    Authorization: `Bearer ${token || customers[i]}`, 'Content-Type': 'application/json',
    ...(key ? { 'Idempotency-Key': key } : {}), ...(version !== undefined ? { 'X-Expected-Version': String(version) } : {})
  } };
}
function key(suffix) { return `${__ENV.RUN_ID}-${warming ? 'warm' : exec.scenario.name}-${iteration()}-${suffix}`; }
function hold(kind, ticketTypeId, seatIds = []) {
  const payload = JSON.stringify({ eventSessionId: session, items: [{ ticketTypeId,
    inventoryMode: seatIds.length ? 'ReservedSeating' : 'GeneralAdmission', quantity: 1, seatIds }] });
  const p = params('hold', key('hold'));
  const response = http.post(`${base}/api/v1/holds`, payload, p);
  const data = observe(response, 'hold', ['INSUFFICIENT_CAPACITY', 'SEAT_UNAVAILABLE']);
  if (response.status !== 201) return null;
  if (!warming) created.add(1, { kind });
  const replay = http.post(`${base}/api/v1/holds`, payload, p);
  const again = observe(replay, 'hold_replay');
  verify(replay, { 'hold retry returns same id': () => again && data && again.id === data.id });
  return data;
}
export function setup() {
  // Readiness, discovery and HTTP warm-up are excluded from measured scenario metrics.
  let ready = false;
  for (let i = 0; i < 60; i++) {
    const r = http.get(`${base}/health`, { tags: { name: 'setup' } });
    if (r.status === 200) { ready = true; break; } sleep(1);
  }
  if (!ready) fail('API health did not become ready');
  const me = http.get(`${base}/api/v1/users/me`, { headers: { Authorization: `Bearer ${customers[0]}` }, tags: { name: 'setup' } });
  if (me.status !== 200) fail(`Load JWT rejected: ${me.status}`);
  for (let i = 0; i < 10; i++) http.get(`${base}/api/v1/events?limit=10`, { tags: { name: 'setup' } });
  for (let i = 0; i < 10; i++) {
    const headers = { Authorization: `Bearer ${customers[9999]}`, 'Content-Type': 'application/json', 'Idempotency-Key': `${__ENV.RUN_ID}-warm-${i}` };
    const r = http.post(`${base}/api/v1/holds`, JSON.stringify({ eventSessionId: session,
      items: [{ ticketTypeId: checkoutType, inventoryMode: 'GeneralAdmission', quantity: 1, seatIds: [] }] }),
      { headers, tags: { name: 'setup' } });
    if (r.status !== 201) fail(`Booking warm-up failed: ${r.status}`);
    const h = r.json().data;
    headers['X-Expected-Version'] = String(h.version);
    const cancelled = http.post(`${base}/api/v1/holds/${h.id}/cancellations`, JSON.stringify({ reason: 'warm-up' }), { headers, tags: { name: 'setup' } });
    if (cancelled.status !== 200) fail(`Warm-up cancellation failed: ${cancelled.status}`);
  }
  warming = true;
  for (warmIteration = 0; warmIteration < 10; warmIteration++) {
    browse(); availability(); checkout();
  }
  warming = false;
}
export function browse() {
  const r = http.get(`${base}/api/v1/events?limit=10&q=Baseline`, { tags: { name: 'browse' } });
  observe(r, 'browse'); if (!warming) cache.add(r.headers['X-Catalog-Cache'] === 'HIT');
  observe(http.get(`${base}/api/v1/events/${event}`, { tags: { name: 'detail' } }), 'detail');
}
export function availability() {
  observe(http.get(`${base}/api/v1/event-sessions/${session}/availability`, { tags: { name: 'availability' } }), 'availability');
}
export function quantity() { hold('quantity', quantityType); }
export function hotseat() { hold('hotseat', seatType, [seat]); }
export function checkout() {
  const h = hold('checkout', checkoutType); if (!h) return;
  const cp = params('checkout', key('checkout'), h.version);
  const order = observe(http.post(`${base}/api/v1/holds/${h.id}/checkout`, '{}', cp), 'checkout');
  if (!order) return;
  const replay = observe(http.post(`${base}/api/v1/holds/${h.id}/checkout`, '{}', cp), 'checkout_replay');
  verify(replay, { 'checkout retry returns same order': () => replay && replay.id === order.id });
  const outcome = iteration() % 2 === 0 ? 'Succeeded' : 'Failed';
  // Use a distinct event ID: the simulator creates its own timestamp and failure
  // reason, so reusing its ID would correctly trigger WEBHOOK_EVENT_CONFLICT.
  const mp = params('payment', key('payment'), order.version);
  const payment = observe(http.post(`${base}/api/v1/orders/${order.id}/payments/mock`, JSON.stringify({ outcome }), mp), 'payment');
  if (!payment) return;
  const paymentReplay = observe(http.post(`${base}/api/v1/orders/${order.id}/payments/mock`, JSON.stringify({ outcome }), mp), 'payment_replay');
  verify(paymentReplay, { 'payment retry returns same attempt': () => paymentReplay && paymentReplay.id === payment.id });
  const ref = payment.id.replace(/-/g, '');
  const callback = JSON.stringify({ providerEventId: `load-event-${ref}`, paymentId: payment.id,
    providerTransactionId: `mock-txn-${ref}`, outcome, amount: payment.amount, currency: payment.currency, occurredAtUtc: new Date().toISOString() });
  for (let i = 0; i < 2; i++) {
    const receipt = observe(http.post(`${base}/api/v1/payment-webhooks/mock`, callback, params('callback', null, undefined, callbackToken)), 'callback');
    verify(receipt, { 'callback settled, replayed, or simulator won': () => receipt &&
      (i === 1 ? receipt.disposition === 'Duplicate' :
       (receipt.disposition === 'Processed' ||
        (receipt.disposition === 'Ignored' && receipt.reason === 'PAYMENT_NOT_PENDING'))) });
  }
  const final = observe(http.get(`${base}/api/v1/payments/${payment.id}`, params('payment_state')), 'payment_state');
  verify(final, { 'payment reached requested outcome': () => final && final.status === outcome });
  if (!warming && final && final.status === outcome) (outcome === 'Succeeded' ? paid : failed).add(1);
  // Failed payments leave the Hold active. Release it so soak measures a stable
  // workload instead of accumulating reservations until the hold TTL.
  if (outcome === 'Failed') {
    observe(http.post(`${base}/api/v1/holds/${h.id}/cancellations`, JSON.stringify({ reason: 'failed load payment' }),
      params('cancel', key('cancel'), h.version)), 'cancel');
  }
}
export function mixed() {
  browse(); availability();
  if (iteration() % 5 === 0) { checkout(); sleep(0.1); return; }
  const h = hold('mixed', checkoutType);
  if (h) {
    observe(http.post(`${base}/api/v1/holds/${h.id}/cancellations`, JSON.stringify({ reason: 'load iteration complete' }),
      params('cancel', key('cancel'), h.version)), 'cancel');
  }
  sleep(0.1);
}
export function handleSummary(data) { return { '/reports/summary.json': JSON.stringify(data, null, 2) }; }
