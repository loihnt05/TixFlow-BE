import { poll, requireValue } from './http.mjs';

export async function verifyTickets(api, peerApi, order, state) {
  const { data } = await poll(() => api(`/api/v1/tickets?orderId=${order.id}`), r => r.data.length > 0);
  requireValue(data.length === 1 && data[0].orderId === order.id && data[0].status === 'Issued', 'Expected exactly one issued ticket');
  const ticket = data[0];
  state.ticketId = ticket.id;
  const { data: qr } = await api(`/api/v1/tickets/${ticket.id}/qr`);
  requireValue(typeof qr.qrToken === 'string' && qr.qrToken.startsWith('tf1_') &&
    qr.qrImageDataUrl?.startsWith('data:image/png;base64,'), 'Ticket QR is not available');
  await peerApi(`/api/v1/tickets/${ticket.id}`, { expected: [404] });
  await peerApi(`/api/v1/tickets/${ticket.id}/qr`, { expected: [404] });
  return { ticket, qrToken: qr.qrToken };
}

export async function verifyNoTickets(api, order) {
  const { data } = await api(`/api/v1/tickets?orderId=${order.id}`);
  requireValue(data.length === 0, 'Unpaid order unexpectedly has tickets');
}
