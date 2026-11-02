using System.Data.Common;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TixFlow.Infrastructure.Persistence;
using TixFlow.Infrastructure.Processing;

namespace TixFlow.Api.Tests;

[Collection("Baseline")]
[Trait("Category", "Baseline")]
public sealed class Phase9PerformanceTests(AuthApiFactory factory)
{
    [Fact]
    public async Task CatalogCacheReducesQueriesAndHoldListsHaveConstantQueryCount()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        var measurements = new List<object>();
        try
        {
            foreach (bool cached in new[] { false, true })
            {
                await data.ResetAsync();
                var counter = new QueryCounter();
                await using var host = factory.WithWebHostBuilder(b =>
                {
                    b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["CatalogCache:Enabled"] = cached.ToString() }));
                    b.ConfigureTestServices(services =>
                    {
                        services.AddHttpContextAccessor();
                        services.RemoveAll<DbContextOptions<TixFlowDbContext>>();
                        services.AddDbContext<TixFlowDbContext>(options => options.UseNpgsql(factory.ConnectionString,
                            p => p.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)).UseSnakeCaseNamingConvention().AddInterceptors(counter));
                    });
                });
                using var client = host.CreateClient();
                foreach (string path in new[] { "/api/v1/events?limit=12", "/api/v1/events/" + BaselineDataset.Event })
                {
                    for (int i = 0; i < 5; i++) { using var warm = await client.GetAsync(path); warm.EnsureSuccessStatusCode(); }
                    var latency = new List<double>(); var queries = new List<long>(); var hits = 0; long bytes = 0;
                    for (int i = 0; i < 50; i++)
                    {
                        // Only HTTP queries count; exclude the simulator background loop.
                        Interlocked.Exchange(ref counter.Total, 0);
                        var clock = Stopwatch.StartNew();
                        using var response = await client.GetAsync(path);
                        response.EnsureSuccessStatusCode();
                        bytes = (await response.Content.ReadAsByteArrayAsync()).Length;
                        latency.Add(clock.Elapsed.TotalMilliseconds); queries.Add(Interlocked.Read(ref counter.Total));
                        if (response.Headers.TryGetValues("X-Catalog-Cache", out var values) && values.Single() == "HIT") hits++;
                    }
                    measurements.Add(new { path, cache = cached, requests = 50, hits, responseBytes = bytes,
                        p50Ms = latency.Order().ElementAt(24), p95Ms = latency.Order().ElementAt(47), queriesMin = queries.Min(), queriesMax = queries.Max() });
                    Assert.All(queries, count => Assert.InRange(count, 1, cached ? 3 : 10));
                }
                if (!cached)
                {
                    client.DefaultRequestHeaders.Authorization = new("Bearer", data.UserToken(1));
                    for (int i = 0; i < 10; i++)
                    {
                        using var hold = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "list-query-" + i);
                        hold.EnsureSuccessStatusCode();
                    }
                    var counts = new List<long>();
                    foreach (int limit in new[] { 1, 10 })
                    {
                        Interlocked.Exchange(ref counter.Total, 0);
                        using var response = await client.GetAsync("/api/v1/holds?limit=" + limit);
                        response.EnsureSuccessStatusCode();
                        counts.Add(Interlocked.Read(ref counter.Total));
                    }
                    Assert.Equal(counts[0], counts[1]);
                    measurements.Add(new { path = "/api/v1/holds", limits = new[] { 1, 10 }, queries = counts });
                }
            }
            // Isolate CPU work removed from each authorized QR read. Database/auth work is unchanged.
            var signer = new QrTokens(); Guid ticket = Guid.NewGuid();
            string token = signer.Create(ticket, "performance", signer.ActiveKeyId);
            byte[] encrypted = signer.RenderEncrypted(ticket, token, signer.ActiveKeyId);
            for (int i = 0; i < 5; i++) { signer.ReadImage(ticket, encrypted, signer.ActiveKeyId); QRCoder.PngByteQRCodeHelper.GetQRCode(token, QRCoder.QRCodeGenerator.ECCLevel.M, 5); }
            foreach (bool renderedByWorker in new[] { false, true })
            {
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++)
                    if (renderedByWorker) signer.ReadImage(ticket, encrypted, signer.ActiveKeyId);
                    else QRCoder.PngByteQRCodeHelper.GetQRCode(token, QRCoder.QRCodeGenerator.ECCLevel.M, 5);
                measurements.Add(new { operation = "QR image", renderedByWorker, iterations = 100, totalMs = clock.Elapsed.TotalMilliseconds });
            }
            string directory = Environment.GetEnvironmentVariable("TIXFLOW_REPORT_DIR") ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "catalog-performance.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { await data.ClearAsync(); }
    }


    private sealed class QueryCounter : DbCommandInterceptor
    {
        public long Total;
        private readonly HttpContextAccessor accessor = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (accessor.HttpContext is not null) Interlocked.Increment(ref Total);
            return ValueTask.FromResult(result);
        }
    }
}
