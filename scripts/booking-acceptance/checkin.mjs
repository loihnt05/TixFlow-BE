import { requireValue } from './http.mjs';

export async function checkIn(organizerApi, customerApi, config, issued) {
  const request = { method: 'POST', key: `${config.runId}:checkin`,
    body: { eventSessionId: config.sessionId, qrToken: issued.qrToken } };
  const { data: first } = await organizerApi('/api/v1/check-ins', request);
  requireValue(first.ticketId === issued.ticket.id && first.status === 'Used', 'Check-in did not mark the expected ticket used');
  const { data: replay } = await organizerApi('/api/v1/check-ins', request);
  requireValue(replay.checkedInAtUtc === first.checkedInAtUtc, 'Check-in replay changed its timestamp');
  const duplicate = await organizerApi('/api/v1/check-ins', { ...request,
    key: `${config.runId}:checkin-duplicate`, expected: [409] });
  requireValue(duplicate.code === 'TICKET_ALREADY_USED', 'Duplicate scan returned an unexpected error');
  const { data: ticket } = await customerApi(`/api/v1/tickets/${issued.ticket.id}`);
  requireValue(ticket.status === 'Used', 'Ticket readback does not show completed check-in');
}
