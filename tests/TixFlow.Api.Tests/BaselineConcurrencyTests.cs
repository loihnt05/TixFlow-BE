using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Npgsql;
using Xunit.Abstractions;

namespace TixFlow.Api.Tests;

[Collection("Baseline")]
[Trait("Category", "Baseline")]
public sealed class BaselineConcurrencyTests(AuthApiFactory factory, ITestOutputHelper output)
{
    [Fact]
    public async Task RepeatedBurstsOf100500And1000NeverOversell()
    {
        var dataset = new BaselineDataset(factory);
        await dataset.ResetAsync();
        using var client = dataset.Client();
        var tokens = Enumerable.Range(1, 1000).Select(dataset.UserToken).ToArray();
        // Warm application/EF/JIT before measurement, then restore exactly the same dataset.
        for (int i = 0; i < 10; i++)
        {
            using var warm = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), $"warm-{i}", token: tokens[i]);
            Assert.Equal(HttpStatusCode.Created, warm.StatusCode);
        }
        var reports = new List<object>();
        try
        {
            for (int repeat = 1; repeat <= 2; repeat++)
            foreach (int count in new[] { 100, 500, 1000 })
            {
                await dataset.ResetAsync();
                await factory.ExecuteSqlAsync("ANALYZE; SELECT pg_stat_statements_reset();");
                using var monitoring = new CancellationTokenSource();
                var samples = new ConcurrentBag<LockSample>();
                Task sampler = SampleLocks(samples, monitoring.Token);
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<Attempt>[] requests = Enumerable.Range(0, count).Select(async i =>
                {
                    await gate.Task;
                    var clock = Stopwatch.StartNew();
                    try
                    {
                        using var response = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(),
                            $"baseline-{i}", token: tokens[i]);
                        string body = await response.Content.ReadAsStringAsync();
                        using var document = JsonDocument.Parse(body);
                        string code = response.IsSuccessStatusCode ? "CREATED" : document.RootElement.GetProperty("code").GetString()!;
                        return new Attempt(i, (int)response.StatusCode, code, clock.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception ex) { return new Attempt(i, 0, ex.GetType().Name, clock.Elapsed.TotalMilliseconds); }
                }).ToArray();
                var elapsed = Stopwatch.StartNew();
                gate.SetResult();
                Attempt[] results = await Task.WhenAll(requests);
                elapsed.Stop();
                monitoring.Cancel();
                await sampler;
                long held = await dataset.Count($"SELECT held_quantity FROM ticket_inventories WHERE ticket_type_id = '{BaselineDataset.Type}'");
                long sold = await dataset.Count($"SELECT sold_quantity FROM ticket_inventories WHERE ticket_type_id = '{BaselineDataset.Type}'");
                long activeUnits = await dataset.Count($"SELECT coalesce(sum(i.quantity),0) FROM hold_items i JOIN holds h ON h.id=i.hold_id WHERE h.status='Active' AND i.ticket_type_id='{BaselineDataset.Type}'");
                long completed = await dataset.Count("SELECT count(*) FROM idempotency_records WHERE key LIKE 'baseline-%' AND is_completed");
                var latencies = results.Select(x => x.Milliseconds).Order().ToArray();
                int created = results.Count(x => x.Status == 201);
                int unavailable = results.Count(x => x.Code == "INSUFFICIENT_CAPACITY");
                var report = new
                {
                    repeat, requests = count, created, unavailable, held, sold, activeUnits, completed,
                    oversold = Math.Max(0, held + sold - 50), durationMs = elapsed.Elapsed.TotalMilliseconds,
                    throughput = count / elapsed.Elapsed.TotalSeconds,
                    p50Ms = Percentile(latencies, .50), p95Ms = Percentile(latencies, .95), p99Ms = Percentile(latencies, .99),
                    responses = results.GroupBy(x => new { x.Status, x.Code }).Select(g => new { g.Key.Status, g.Key.Code, count = g.Count() }),
                    lockSamples = samples.ToArray(), statements = await Statements(), plan = await InventoryPlan(), attempts = results
                };
                reports.Add(report);
                await WriteReport(reports);
                output.WriteLine($"Repeat {repeat}, N={count}: created={created}, capacity conflicts={unavailable}, held={held}, sold={sold}, p95={report.p95Ms:F1}ms");
                Assert.Equal(50, created);
                Assert.Equal(count - 50, unavailable);
                Assert.Equal(50, held);
                Assert.Equal(0, sold);
                Assert.Equal(held, activeUnits);
                Assert.Equal(count, completed);
            }
        }
        finally { await dataset.ClearAsync(); }
    }

    private static double Percentile(double[] values, double fraction) => values[(int)Math.Ceiling(values.Length * fraction) - 1];

    private async Task SampleLocks(ConcurrentBag<LockSample> samples, CancellationToken ct)
    {
        // Separate one-connection observer pool, so observing cannot occupy the API's 40 slots.
        var cs = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { ApplicationName = "baseline-observer", MaxPoolSize = 1 };
        await using var connection = new NpgsqlConnection(cs.ConnectionString);
        await connection.OpenAsync(ct);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await using var command = new NpgsqlCommand("""
                    SELECT count(*) FILTER (WHERE state = 'active'),
                           count(*) FILTER (WHERE wait_event_type = 'Lock'),
                           count(*) FILTER (WHERE wait_event_type = 'Lock' AND query LIKE '%FROM events%FOR UPDATE%'),
                           coalesce(max(extract(epoch from clock_timestamp()-query_start)) FILTER (WHERE wait_event_type='Lock'),0)
                    FROM pg_stat_activity WHERE application_name = 'tixflow-baseline-api'
                    """, connection);
                await using (var reader = await command.ExecuteReaderAsync(ct))
                {
                    await reader.ReadAsync(ct);
                    samples.Add(new(DateTimeOffset.UtcNow, reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetDecimal(3)));
                }
                await Task.Delay(25, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task<JsonElement> Statements()
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT coalesce(json_agg(s), '[]')::text FROM
              (SELECT query, calls, total_exec_time, mean_exec_time, rows, shared_blks_hit, shared_blks_read
               FROM pg_stat_statements WHERE dbid = (SELECT oid FROM pg_database WHERE datname=current_database())
               ORDER BY total_exec_time DESC LIMIT 15) s
            """, connection);
        return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!).RootElement.Clone();
    }

    private async Task<JsonElement> InventoryPlan()
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) SELECT * FROM ticket_inventories WHERE ticket_type_id='{BaselineDataset.Type}' FOR UPDATE", connection);
        return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!).RootElement.Clone();
    }

    private static Task WriteReport(List<object> reports)
    {
        string directory = Environment.GetEnvironmentVariable("TIXFLOW_REPORT_DIR") ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        return File.WriteAllTextAsync(Path.Combine(directory, "concurrency.json"), JsonSerializer.Serialize(new
        {
            baseline = "baseline-v1", sourceRevision = Environment.GetEnvironmentVariable("TIXFLOW_SOURCE_REVISION") ?? "working-tree",
            utc = DateTimeOffset.UtcNow, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, processors = Environment.ProcessorCount,
            transport = "ASP.NET TestServer (in-process HTTP pipeline)", apiInstances = 1, poolMax = 40,
            cache = "off; Redis absent", databaseImage = AuthApiFactory.PostgresImage, brokerImage = AuthApiFactory.RabbitImage,
            results = reports
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record Attempt(int Index, int Status, string Code, double Milliseconds);
    private sealed record LockSample(DateTimeOffset At, long Active, long Waiting, long EventRowWaiting, decimal LongestWaitSeconds);
}
