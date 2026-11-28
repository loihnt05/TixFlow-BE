import { poll, requireValue } from './http.mjs';

export async function pay(api, config, order, state) {
  const outcome = config.outcome ?? 'Succeeded';
  requireValue(['Succeeded', 'Failed', 'Timeout'].includes(outcome), 'Unsupported mock payment outcome');
  const path = `/api/v1/orders/${order.id}/payments/mock`;
  const request = { method: 'POST', key: `${config.runId}:payment`, version: order.version,
    body: { outcome }, expected: [200, 201] };
  const { data: payment } = await api(path, request);
  state.paymentId = payment.id;
  requireValue(payment.orderId === order.id && payment.amount === order.totalAmount &&
    payment.currency === order.currency, 'Payment differs from order');
  const { data: replay } = await api(path, request);
  requireValue(replay.id === payment.id, 'Payment replay created another attempt');
  const terminal = await poll(() => api(`/api/v1/payments/${payment.id}`), r => r.data.status !== 'Pending');
  requireValue(terminal.data.status === (outcome === 'Succeeded' ? 'Succeeded' : 'Failed'), 'Unexpected payment outcome');
  const { data: updated } = await api(`/api/v1/orders/${order.id}`);
  requireValue(outcome === 'Succeeded' ? updated.status === 'Paid' : updated.status !== 'Paid', 'Order state contradicts payment');
  return updated;
}
