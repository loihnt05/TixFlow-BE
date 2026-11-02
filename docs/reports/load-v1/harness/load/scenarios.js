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
  systemTags: ['status', 'method', 'name', 'scenario', 'expected_response'],
};
http.setResponseCallback(http.expectedStatuses(200, 201, 409, 429));
function body(r) { try { return r.json(); } catch (_) { return {}; } }
function observe(r, endpoint, conflicts = []) {
  const data = body(r);
  const conflict = r.status === 409 && conflicts.includes(data.code);
  const limited = r.status === 429;
  const ok = r.status === 200 || r.status === 201 || conflict || limited;
  technical.add(!ok, { endpoint }); business.add(conflict, { endpoint }); admission.add(limited, { endpoint });
  latency.add(r.timings.duration, { endpoint });
  check(r, { 'expected HTTP contract': () => ok, '429 has Retry-After': () => !limited || !!r.headers['Retry-After'] });
  return data.data;
}
function params(endpoint, key, version, token) {
  const i = exec.scenario.iterationInTest % customers.length;
  return { timeout: '60s', tags: { name: endpoint }, headers: {
    Authorization: `Bearer ${token || customers[i]}`, 'Content-Type': 'application/json',
    ...(key ? { 'Idempotency-Key': key } : {}), ...(version !== undefined ? { 'X-Expected-Version': String(version) } : {})
  } };
}
function key(suffix) { return `${__ENV.RUN_ID}-${exec.scenario.name}-${exec.scenario.iterationInTest}-${suffix}`; }
function hold(kind, ticketTypeId, seatIds = []) {
  const payload = JSON.stringify({ eventSessionId: session, items: [{ ticketTypeId,
    inventoryMode: seatIds.length ? 'ReservedSeating' : 'GeneralAdmission', quantity: 1, seatIds }] });
  const p = params('hold', key('hold'));
  const response = http.post(`${base}/api/v1/holds`, payload, p);
  const data = observe(response, 'hold', ['INSUFFICIENT_CAPACITY', 'SEAT_UNAVAILABLE']);
  if (response.status !== 201) return null;
  created.add(1, { kind });
  const replay = http.post(`${base}/api/v1/holds`, payload, p);
  const again = observe(replay, 'hold_replay');
  check(replay, { 'hold retry returns same id': () => again && data && again.id === data.id });
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
}
export function browse() {
  const r = http.get(`${base}/api/v1/events?limit=10&q=Baseline`, { tags: { name: 'browse' } });
  observe(r, 'browse'); cache.add(r.headers['X-Catalog-Cache'] === 'HIT');
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
  check(replay, { 'checkout retry returns same order': () => replay && replay.id === order.id });
  const outcome = exec.scenario.iterationInTest % 2 === 0 ? 'Succeeded' : 'Failed';
  // Use the simulator's canonical event/reference so either arrival order is a replay.
  const mp = params('payment', key('payment'), order.version);
  const payment = observe(http.post(`${base}/api/v1/orders/${order.id}/payments/mock`, JSON.stringify({ outcome }), mp), 'payment');
  if (!payment) return;
  const paymentReplay = observe(http.post(`${base}/api/v1/orders/${order.id}/payments/mock`, JSON.stringify({ outcome }), mp), 'payment_replay');
  check(paymentReplay, { 'payment retry returns same attempt': () => paymentReplay && paymentReplay.id === payment.id });
  const ref = payment.id.replace(/-/g, '');
  const callback = JSON.stringify({ providerEventId: `mock-event-${ref}`, paymentId: payment.id,
    providerTransactionId: `mock-txn-${ref}`, outcome, amount: payment.amount, currency: payment.currency, occurredAtUtc: new Date().toISOString() });
  for (let i = 0; i < 2; i++) {
    const receipt = observe(http.post(`${base}/api/v1/payment-webhooks/mock`, callback, params('callback', null, undefined, callbackToken)), 'callback');
    check(receipt, { 'callback processed or replayed': () => receipt && ['Processed', 'Duplicate'].includes(receipt.disposition) });
  }
  const final = observe(http.get(`${base}/api/v1/payments/${payment.id}`, params('payment_state')), 'payment_state');
  check(final, { 'payment reached requested outcome': () => final && final.status === outcome });
  if (final && final.status === outcome) (outcome === 'Succeeded' ? paid : failed).add(1);
}
export function mixed() {
  browse(); availability();
  const h = hold('mixed', checkoutType);
  if (h) {
    observe(http.post(`${base}/api/v1/holds/${h.id}/cancellations`, JSON.stringify({ reason: 'load iteration complete' }),
      params('cancel', key('cancel'), h.version)), 'cancel');
  }
  sleep(0.1);
}
export function handleSummary(data) { return { '/reports/summary.json': JSON.stringify(data, null, 2) }; }
