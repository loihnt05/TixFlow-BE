import test from 'node:test';
import assert from 'node:assert/strict';
import { createClient, poll } from '../scripts/booking-acceptance/http.mjs';
import { verifyCatalog } from '../scripts/booking-acceptance/catalog.mjs';
import { createHold } from '../scripts/booking-acceptance/hold.mjs';
import { checkout } from '../scripts/booking-acceptance/checkout.mjs';
import { pay } from '../scripts/booking-acceptance/payment.mjs';
import { verifyTickets, verifyNoTickets } from '../scripts/booking-acceptance/tickets.mjs';
import { checkIn } from '../scripts/booking-acceptance/checkin.mjs';
import { cancelHold } from '../scripts/booking-acceptance/cancel.mjs';

const config = { runId: 'run', eventId: 'event', sessionId: 'session', ticketTypeId: 'type' };
const hold = { id: 'hold', status: 'Active', eventSessionId: 'session', version: 0,
  totalAmount: 100, currency: 'VND', expiresAtUtc: '2099-01-01T00:00:00Z',
  items: [{ ticketTypeId: 'type', quantity: 1 }] };
const order = { id: 'order', status: 'PendingPayment', totalAmount: 100, currency: 'VND', version: 0 };
function sequence(rows) {
  let index = 0;
  const api = async (path, options = {}) => {
    assert.ok(index < rows.length, `Unexpected call: ${path}`);
    const row = rows[index++];
    assert.equal(path, row.path);
    row.check?.(options);
    return structuredClone(row.result);
  };
  api.done = () => assert.equal(index, rows.length);
  return api;
}

test('HTTP records status only and disables redirects', async () => {
  const evidence = [];
  const api = createClient('http://localhost:8080', 'secret-token', evidence, async (_url, options) => {
    assert.equal(options.redirect, 'error');
    assert.equal(options.headers.Authorization, 'Bearer secret-token');
    return new Response(JSON.stringify({ qrToken: 'secret-qr' }), { status: 200 });
  });
  await api('/api/v1/tickets/id/qr');
  assert.doesNotMatch(JSON.stringify(evidence), /secret/);
  assert.equal(evidence[0].passed, true);
});

test('HTTP does not retry a failed mutation or expose network errors', async () => {
  let calls = 0;
  const evidence = [];
  const api = createClient('http://localhost:8080', 'secret', evidence, async () => {
    calls++; throw new Error('secret-token');
  });
  await assert.rejects(api('/api/v1/holds', { method: 'POST' }), /Request failed/);
  assert.equal(calls, 1);
  assert.doesNotMatch(JSON.stringify(evidence), /secret/);
  assert.throws(() => createClient('http://example.com', 'secret', []), /Invalid API origin/);
});

test('polling stops at its bound', async () => {
  let calls = 0;
  await assert.rejects(poll(async () => ++calls, () => false, { attempts: 3, intervalMs: 0 }), /Timed out/);
  assert.equal(calls, 3);
});

test('catalog rejects closed sales before any reservation', async () => {
  await assert.rejects(verifyCatalog(async () => ({ id: 'event', status: 'Published',
    saleEndsAtUtc: '2020-01-01T00:00:00Z' }), config), /on sale/);
});

test('hold replay preserves key, payload and resource identity', async () => {
  let first;
  const api = sequence([
    { path: '/api/v1/holds', result: { data: hold }, check: opts => { first = structuredClone(opts); } },
    { path: '/api/v1/holds', result: { data: hold }, check: opts => assert.deepEqual(opts, first) },
  ]);
  const state = {};
  await createHold(api, config, state);
  assert.equal(state.holdId, 'hold'); api.done();
});

test('checkout detects an amount change and retains order ID for diagnosis', async () => {
  const state = {};
  await assert.rejects(checkout(async () => ({ data: { ...order, totalAmount: 101 } }), config, hold, state), /amount/);
  assert.equal(state.orderId, 'order');
});

for (const outcome of ['Succeeded', 'Failed', 'Timeout']) {
  test(`mock payment verifies ${outcome} terminal state`, async () => {
    const payment = { id: 'payment', orderId: 'order', amount: 100, currency: 'VND' };
    const api = sequence([
      { path: '/api/v1/orders/order/payments/mock', result: { data: payment },
        check: opts => { assert.equal(opts.body.outcome, outcome); assert.equal(opts.version, 0); } },
      { path: '/api/v1/orders/order/payments/mock', result: { data: payment } },
      { path: '/api/v1/payments/payment', result: { data: { status: outcome === 'Succeeded' ? 'Succeeded' : 'Failed' } } },
      { path: '/api/v1/orders/order', result: { data: { ...order, status: outcome === 'Succeeded' ? 'Paid' : 'PendingPayment' } } },
    ]);
    await pay(api, { ...config, outcome }, order, {}); api.done();
  });
}

test('issued ticket checks peer access without saving QR to resource ledger', async () => {
  const state = {};
  const api = sequence([
    { path: '/api/v1/tickets?orderId=order', result: { data: [{ id: 'ticket', orderId: 'order', status: 'Issued' }] } },
    { path: '/api/v1/tickets/ticket/qr', result: { data: { qrToken: 'tf1_secret', qrImageDataUrl: 'data:image/png;base64,AA==' } } },
  ]);
  const peer = sequence([
    { path: '/api/v1/tickets/ticket', result: {}, check: opts => assert.deepEqual(opts.expected, [404]) },
    { path: '/api/v1/tickets/ticket/qr', result: {}, check: opts => assert.deepEqual(opts.expected, [404]) },
  ]);
  await verifyTickets(api, peer, order, state);
  assert.deepEqual(state, { ticketId: 'ticket' }); api.done(); peer.done();
  await assert.rejects(verifyNoTickets(async () => ({ data: [{}] }), order), /Unpaid/);
});

test('check-in distinguishes replay from a duplicate scan', async () => {
  const used = { ticketId: 'ticket', status: 'Used', checkedInAtUtc: '2026-01-01T00:00:00Z' };
  const organizer = sequence([
    { path: '/api/v1/check-ins', result: { data: used }, check: opts => assert.equal(opts.key, 'run:checkin') },
    { path: '/api/v1/check-ins', result: { data: used }, check: opts => assert.equal(opts.key, 'run:checkin') },
    { path: '/api/v1/check-ins', result: { code: 'TICKET_ALREADY_USED' },
      check: opts => { assert.equal(opts.key, 'run:checkin-duplicate'); assert.deepEqual(opts.expected, [409]); } },
  ]);
  await checkIn(organizer, async () => ({ data: { status: 'Used' } }), config, { ticket: { id: 'ticket' }, qrToken: 'secret' });
  organizer.done();
});

test('cancellation detects double release through inventory readback', async () => {
  const baseline = { availableQuantity: 1, heldQuantity: 0, soldQuantity: 0 };
  const api = sequence([
    { path: '/api/v1/holds/hold/cancellations', result: { data: { ...hold, status: 'Cancelled', version: 1 } } },
    { path: '/api/v1/holds/hold/cancellations', result: { data: { ...hold, status: 'Cancelled', version: 1 } } },
    { path: '/api/v1/ticket-types/type/availability', result: { ...baseline, availableQuantity: 2 } },
  ]);
  await assert.rejects(cancelHold(api, config, hold, baseline), /Inventory/); api.done();
});
