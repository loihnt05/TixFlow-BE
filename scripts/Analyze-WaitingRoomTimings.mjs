// Summarize per-request timing diagnostics from an isolated Waiting Room load run.
import fs from 'node:fs';
import path from 'node:path';

const directory = process.argv[2];
if (!directory) throw new Error('Usage: node scripts/Analyze-WaitingRoomTimings.mjs <run directory>');
const byRequest = new Map();
for (const line of fs.readFileSync(path.join(directory, 'services.log'), 'utf8').split(/\r?\n/)) {
  const start = line.indexOf('| {');
  if (start < 0) continue;
  let log;
  try { log = JSON.parse(line.slice(start + 2)); } catch { continue; }
  const values = log.Properties || {};
  const id = values.CorrelationId;
  if (!id) continue;
  const row = byRequest.get(id) || {};
  if (log.MessageTemplate?.startsWith('WaitingRoomTiming identity')) {
    row.path = values.Path;
    row.identityMs = values.ElapsedMs;
  } else if (log.MessageTemplate?.startsWith('WaitingRoomTiming status')) {
    row.redisMs = values.RedisMs;
    row.saleDbMs = values.SaleDbMs;
  } else if (log.MessageTemplate?.startsWith('HTTP ')) {
    row.route = values.Route;
    row.totalMs = values.ElapsedMs;
    row.statusCode = values.StatusCode;
  }
  byRequest.set(id, row);
}
const percentile = (values, percent) => {
  const sorted = values.filter(Number.isFinite).sort((a, b) => a - b);
  return sorted.length ? Math.round(sorted[Math.ceil(sorted.length * percent) - 1] * 100) / 100 : null;
};
const stats = (rows, key) => ({ count: rows.filter(row => Number.isFinite(row[key])).length,
  p50: percentile(rows.map(row => row[key]), .5),
  p95: percentile(rows.map(row => row[key]), .95),
  p99: percentile(rows.map(row => row[key]), .99) });
const status = [...byRequest.values()].filter(row => row.route === '/api/v1/waiting-room/status'
  && Number.isFinite(row.totalMs));
const holds = [...byRequest.values()].filter(row => row.route === '/api/v1/holds/'
  && Number.isFinite(row.totalMs));
for (const row of status) {
  if (Number.isFinite(row.identityMs) && Number.isFinite(row.redisMs) && Number.isFinite(row.saleDbMs))
    row.otherMs = Math.max(0, row.totalMs - row.identityMs - row.redisMs - row.saleDbMs);
}
for (const row of holds) {
  if (Number.isFinite(row.identityMs)) row.afterIdentityMs = Math.max(0, row.totalMs - row.identityMs);
}
const report = { run: path.basename(directory), statusRequests: status.length, holdRequests: holds.length,
  status: Object.fromEntries(['totalMs', 'identityMs', 'redisMs', 'saleDbMs', 'otherMs']
    .map(key => [key, stats(status, key)])),
  hold: Object.fromEntries(['totalMs', 'identityMs', 'afterIdentityMs']
    .map(key => [key, stats(holds, key)])) };
const output = path.join(directory, 'timings.json');
fs.writeFileSync(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
