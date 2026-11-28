import { requireValue } from './http.mjs';

export async function verifyCatalog(api, { eventId, sessionId, ticketTypeId }, now = Date.now()) {
  const event = await api(`/api/v1/events/${eventId}`);
  const session = event.sessions?.find(item => item.id === sessionId);
  const ticket = session?.ticketTypes?.find(item => item.id === ticketTypeId);
  const inSale = item => (!item.saleStartsAtUtc || Date.parse(item.saleStartsAtUtc) <= now) &&
    (!item.saleEndsAtUtc || Date.parse(item.saleEndsAtUtc) > now);
  requireValue(event.id === eventId && event.status === 'Published' && inSale(event), 'Event must be published and on sale');
  requireValue(session?.status === 'OnSale' && Date.parse(session.startsAtUtc) > now, 'Session must be on sale and in the future');
  requireValue(ticket?.inventoryMode === 'GeneralAdmission' && inSale(ticket) && ticket.maxPerOrder >= 1,
    'This acceptance runner requires a purchasable GA ticket');
  const availability = await api(`/api/v1/ticket-types/${ticketTypeId}/availability`);
  requireValue(availability.eventSessionId === sessionId && availability.availableQuantity >= 1, 'Fixture has no available ticket');
  requireValue(availability.heldQuantity >= 0 && availability.soldQuantity >= 0 &&
    availability.heldQuantity + availability.soldQuantity <= availability.totalQuantity, 'Invalid inventory counters');
  return { ticket, session, availability };
}
