import { requireValue } from './http.mjs';

export async function cancelHold(api, config, hold, baseline) {
  const path = `/api/v1/holds/${hold.id}/cancellations`;
  const request = { method: 'POST', key: `${config.runId}:cancel`, version: hold.version,
    body: { reason: 'Acceptance cancellation and release verification' } };
  const { data: cancelled } = await api(path, request);
  requireValue(cancelled.id === hold.id && cancelled.status === 'Cancelled', 'Hold was not cancelled');
  const { data: replay } = await api(path, request);
  requireValue(replay.id === hold.id && replay.version === cancelled.version, 'Cancellation replay changed the hold');
  const after = await api(`/api/v1/ticket-types/${config.ticketTypeId}/availability`);
  requireValue(after.availableQuantity === baseline.availableQuantity &&
    after.heldQuantity === baseline.heldQuantity && after.soldQuantity === baseline.soldQuantity,
    'Inventory did not return to baseline; use a fixture without concurrent writers');
}
