import { parseArgs } from 'node:util';
import { randomUUID } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { createClient, requireValue } from './booking-acceptance/http.mjs';
import { verifyCatalog } from './booking-acceptance/catalog.mjs';
import { createHold } from './booking-acceptance/hold.mjs';
import { checkout } from './booking-acceptance/checkout.mjs';
import { pay } from './booking-acceptance/payment.mjs';
import { verifyTickets, verifyNoTickets } from './booking-acceptance/tickets.mjs';
import { checkIn } from './booking-acceptance/checkin.mjs';
import { cancelHold } from './booking-acceptance/cancel.mjs';

const { values } = parseArgs({ options: {
  execute: { type: 'boolean', default: false }, checkin: { type: 'boolean', default: false },
  'base-url': { type: 'string', default: 'http://localhost:8080' },
  event: { type: 'string' }, session: { type: 'string' }, ticket: { type: 'string' },
  scenario: { type: 'string', default: 'purchase' },
} });
requireValue(values.execute, 'Use --execute to create transactions in an isolated acceptance environment');
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
for (const name of ['event', 'session', 'ticket']) requireValue(uuid.test(values[name] ?? ''), `Missing or invalid --${name}`);
requireValue(['purchase', 'cancel', 'failed', 'timeout'].includes(values.scenario), 'Invalid scenario');
requireValue(!values.checkin || values.scenario === 'purchase', '--checkin requires purchase');
const runId = randomUUID();
const config = { runId, eventId: values.event, sessionId: values.session, ticketTypeId: values.ticket,
  outcome: values.scenario === 'failed' ? 'Failed' : values.scenario === 'timeout' ? 'Timeout' : 'Succeeded' };
const report = { runId, startedAtUtc: new Date().toISOString(), scenario: values.scenario,
  checkinRequested: values.checkin, status: 'running', stage: 'identity', resources: {}, requests: [] };
const directory = fileURLToPath(new URL(`../artifacts/acceptance/booking-${runId}/`, import.meta.url));
await mkdir(directory, { recursive: true });
try {
  const api = createClient(values['base-url'], process.env.TIXFLOW_CUSTOMER_A_TOKEN, report.requests);
  const peer = createClient(values['base-url'], process.env.TIXFLOW_CUSTOMER_B_TOKEN, report.requests);
  const me = await api('/api/v1/users/me');
  const other = await peer('/api/v1/users/me');
  requireValue(me.roles?.length === 1 && me.roles[0] === 'Customer' && other.roles?.length === 1 &&
    other.roles[0] === 'Customer' && me.sub !== other.sub && me.id !== other.id, 'Two distinct Customer identities required');
  let organizer;
  if (values.checkin) {
    organizer = createClient(values['base-url'], process.env.TIXFLOW_ORGANIZER_A_TOKEN, report.requests);
    const actor = await organizer('/api/v1/users/me');
    requireValue(actor.roles?.length === 1 && actor.roles[0] === 'Organizer', 'Organizer identity required');
    await organizer(`/api/v1/management/events/${config.eventId}`);
  }
  report.stage = 'catalog';
  const fixture = await verifyCatalog(api, config);
  if (values.checkin) requireValue(Date.now() >= Date.parse(fixture.session.startsAtUtc) - 3600000 &&
    Date.now() < Date.parse(fixture.session.endsAtUtc), 'Check-in window must be open before starting this run');
  report.stage = 'hold';
  const hold = await createHold(api, config, report.resources);
  if (values.scenario === 'cancel') {
    report.stage = 'cancel';
    await cancelHold(api, config, hold, fixture.availability);
  } else {
    report.stage = 'checkout';
    const order = await checkout(api, config, hold, report.resources);
    report.stage = 'payment';
    const updated = await pay(api, config, order, report.resources);
    report.stage = 'tickets';
    if (config.outcome === 'Succeeded') {
      const issued = await verifyTickets(api, peer, updated, report.resources);
      if (values.checkin) {
        report.stage = 'checkin';
        await checkIn(organizer, api, config, issued);
      }
    } else await verifyNoTickets(api, updated);
  }
  report.status = 'passed';
} catch {
  report.status = 'failed';
  process.exitCode = 1;
  console.error(`Booking acceptance failed at ${report.stage}. See HTTP evidence and resource IDs.`);
} finally {
  report.completedAtUtc = new Date().toISOString();
  await writeFile(`${directory}report.json`, JSON.stringify(report, null, 2), { flag: 'wx' });
  console.log(`Evidence: ${directory}report.json`);
}
