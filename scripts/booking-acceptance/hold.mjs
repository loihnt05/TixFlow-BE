import { requireValue } from './http.mjs';

export async function createHold(api, config, state) {
  const request = { method: 'POST', key: `${config.runId}:hold`, expected: [200, 201],
    body: { eventSessionId: config.sessionId, items: [{ ticketTypeId: config.ticketTypeId,
      inventoryMode: 'GeneralAdmission', quantity: 1, seatIds: [] }] } };
  const { data: hold } = await api('/api/v1/holds', request);
  state.holdId = hold.id;
  requireValue(hold.status === 'Active' && hold.eventSessionId === config.sessionId &&
    Date.parse(hold.expiresAtUtc) > Date.now(), 'Hold is not active for the selected session');
  requireValue(hold.items?.length === 1 && hold.items[0].ticketTypeId === config.ticketTypeId &&
    hold.items[0].quantity === 1, 'Hold selection differs from request');
  const { data: replay } = await api('/api/v1/holds', request);
  requireValue(replay.id === hold.id && replay.totalAmount === hold.totalAmount, 'Hold replay created a different reservation');
  return hold;
}
