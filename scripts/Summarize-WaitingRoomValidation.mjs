// Audit an isolated three-pair Waiting Room A/B campaign.
import fs from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const outputIndex = args.indexOf('--output');
let output;
if (outputIndex >= 0) {
  output = args[outputIndex + 1];
  args.splice(outputIndex, 2);
}
if (args.length !== 6 || (outputIndex >= 0 && !output)) {
  throw new Error('Usage: node scripts/Summarize-WaitingRoomValidation.mjs <six run directories> [--output file]');
}
const read = (directory, name) => JSON.parse(fs.readFileSync(path.join(directory, name), 'utf8').replace(/^\uFEFF/, ''));
const value = (summary, name, key, fallback = null) => summary.metrics?.[name]?.values?.[key] ?? fallback;
const keys = ['image', 'revision', 'harnessInputs', 'imageInputs', 'offeredArrivals',
  'arrivalWindowSeconds', 'grantsPerSecond', 'apiReplicas', 'apiCpuEach',
  'apiMemoryBytesEach', 'postgresCpu', 'postgresMemoryBytes', 'dataset', 'diagnosticsEnabled'];
const identity = manifest => Object.fromEntries(keys.map(key => [key, key.endsWith('Inputs')
  ? manifest[key].map(item => ({ path: item.path, sha256: item.sha256 })).sort((a, b) => a.path.localeCompare(b.path))
  : manifest[key]]));
const median = (rows, key) => {
  const values = rows.map(row => row[key]).filter(item => item !== null).sort((a, b) => a - b);
  if (!values.length) return null;
  const middle = values.length >> 1;
  return Math.round(((values.length % 2 ? values[middle] : (values[middle - 1] + values[middle]) / 2)) * 100) / 100;
};

const loaded = args.map(directory => {
  const manifest = read(directory, 'manifest.json');
  const result = read(directory, 'result.json');
  const summary = read(directory, 'summary.json');
  const invariants = read(directory, 'invariants.json');
  return { manifest, row: {
    directory, runId: manifest.runId, mode: manifest.mode, passed: result.passed,
    joins: value(summary, 'wr_joins', 'count', 0),
    admitted: value(summary, 'wr_admitted', 'count', 0),
    holds: value(summary, 'wr_holds_created', 'count', 0),
    capacityConflicts: value(summary, 'wr_capacity_conflicts', 'count', 0),
    paid: value(summary, 'wr_paid_orders', 'count', 0),
    tickets: value(summary, 'wr_tickets_issued', 'count', 0),
    technicalErrorRate: value(summary, 'wr_technical_errors', 'rate', 0),
    droppedIterations: value(summary, 'dropped_iterations', 'count', 0),
    failedChecks: value(summary, 'checks', 'fails', 0),
    statusP95Ms: value(summary, 'wr_status_latency_ms', 'p(95)'),
    statusP99Ms: value(summary, 'wr_status_latency_ms', 'p(99)'),
    holdP95Ms: value(summary, 'wr_hold_latency_ms', 'p(95)'),
    holdP99Ms: value(summary, 'wr_hold_latency_ms', 'p(99)'),
    decisionP95Ms: value(summary, 'wr_join_to_terminal_ms', 'p(95)'),
    grantWaitP95Ms: value(summary, 'wr_grant_wait_ms', 'p(95)'),
    grantWaitMaxMs: value(summary, 'wr_grant_wait_ms', 'max'),
    paidToTicketP95Ms: value(summary, 'wr_paid_to_ticket_ms', 'p(95)'),
    joinToTicketP95Ms: value(summary, 'wr_join_to_ticket_ms', 'p(95)'),
    queueRemaining: result.queueRemaining,
    oversold: invariants.oversold,
    missingPaidTickets: invariants.missing_paid_tickets,
  } };
});
const rows = loaded.map(item => item.row);
const baseline = identity(loaded[0].manifest);
const sameInputs = loaded.every(item => JSON.stringify(identity(item.manifest)) === JSON.stringify(baseline));
const runCounts = Object.fromEntries(['Active', 'Disabled'].map(mode => [mode, rows.filter(row => row.mode === mode).length]));
// Locked before the next independent campaign; these are local profile targets,
// including a ticket-delivery target chosen from the earlier diagnostic runs.
const latencyTargets = { statusP95Ms: 500, holdP95Ms: 3000, holdP99Ms: 6000,
  decisionP95Ms: 30000, paidToTicketP95Ms: 15000 };
for (const row of rows) {
  row.latencyPassed = row.paidToTicketP95Ms !== null
    && row.paidToTicketP95Ms <= latencyTargets.paidToTicketP95Ms
    && (row.mode !== 'Active' || (row.holdP95Ms !== null
      && row.holdP95Ms <= latencyTargets.holdP95Ms
      && row.holdP99Ms !== null && row.holdP99Ms <= latencyTargets.holdP99Ms
      && row.statusP95Ms !== null
      && row.statusP95Ms <= latencyTargets.statusP95Ms
      && row.decisionP95Ms !== null && row.decisionP95Ms <= latencyTargets.decisionP95Ms));
}
const validRuns = rows.every(row => row.passed && row.joins === 100 && row.droppedIterations === 0
  && row.failedChecks === 0 && row.technicalErrorRate < .01 && row.queueRemaining === 0
  && row.oversold === 0 && row.missingPaidTickets === 0
  && row.admitted === (row.mode === 'Active' ? 100 : 0)
  && row.holds === 50 && row.capacityConflicts === 50 && row.paid === 50 && row.tickets === 50
  && row.paidToTicketP95Ms !== null);
const medianKeys = ['statusP95Ms', 'statusP99Ms', 'holdP95Ms', 'holdP99Ms',
  'decisionP95Ms', 'grantWaitP95Ms', 'grantWaitMaxMs', 'paidToTicketP95Ms', 'joinToTicketP95Ms'];
const report = {
  sameInputs, runCounts,
  valid: validRuns && sameInputs && runCounts.Active === 3 && runCounts.Disabled === 3,
  latencyTargets,
  latencyPassed: rows.every(row => row.latencyPassed),
  configuration: baseline,
  median: Object.fromEntries(['Active', 'Disabled'].map(mode => {
    const group = rows.filter(row => row.mode === mode);
    return [mode, Object.fromEntries(medianKeys.map(key => [key, median(group, key)]))];
  })),
  runs: rows,
};
const text = `${JSON.stringify(report, null, 2)}\n`;
if (output) {
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.writeFileSync(output, text);
}
process.stdout.write(text);
if (!report.valid || !report.latencyPassed) process.exitCode = 1;
