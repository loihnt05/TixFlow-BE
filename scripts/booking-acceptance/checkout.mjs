import { requireValue } from './http.mjs';

export async function checkout(api, config, hold, state) {
  const path = `/api/v1/holds/${hold.id}/checkout`;
  const request = { method: 'POST', key: `${config.runId}:checkout`, version: hold.version, expected: [200, 201] };
  const { data: order } = await api(path, request);
  state.orderId = order.id;
  requireValue(order.status === 'PendingPayment' && order.totalAmount === hold.totalAmount &&
    order.currency === hold.currency, 'Checkout changed the hold amount or currency');
  const { data: replay } = await api(path, request);
  requireValue(replay.id === order.id, 'Checkout replay created a different order');
  const { data: persisted } = await api(`/api/v1/orders/${order.id}`);
  requireValue(persisted.id === order.id && persisted.totalAmount === order.totalAmount, 'Order readback differs from checkout');
  return order;
}
