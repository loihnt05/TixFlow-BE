using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TixFlow.Infrastructure.Processing;

namespace TixFlow.Api.Tests;

[Collection("Integration")]
public sealed class Phase9Tests(AuthApiFactory factory)
{
    [Fact]
    public async Task WorkerBackfillsLegacyQrImagesWithoutChangingTheToken()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        using var client = data.Client();
        try
        {
            using var holdResponse = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "backfill-hold");
            Guid hold = (await BaselineDataset.Data(holdResponse)).GetProperty("id").GetGuid();
            using var checkout = await BaselineDataset.Post(client, $"/api/v1/holds/{hold}/checkout", null, "backfill-checkout");
            Guid order = (await BaselineDataset.Data(checkout)).GetProperty("id").GetGuid();
            using var payment = await BaselineDataset.Post(client, $"/api/v1/orders/{order}/payments/mock", new { outcome = "Succeeded" }, "backfill-payment");
            payment.EnsureSuccessStatusCode();
            for (int i = 0; i < 100 && await data.Count($"SELECT count(*) FROM orders WHERE id='{order}' AND status='Paid'") == 0; i++) await Task.Delay(100);
            var delivery = new TicketDelivery(factory.ConnectionString);
            await delivery.ProcessAsync(new(Guid.NewGuid(), "OrderPaid", JsonSerializer.Serialize(new { orderId = order }), "backfill"));
            using var list = await client.GetAsync($"/api/v1/tickets?orderId={order}");
            Guid ticket = (await BaselineDataset.Data(list))[0].GetProperty("id").GetGuid();
            using var originalResponse = await client.GetAsync($"/api/v1/tickets/{ticket}/qr");
            var original = await BaselineDataset.Data(originalResponse);
            await factory.ExecuteSqlAsync($"UPDATE tickets SET qr_image=NULL WHERE id='{ticket}'");
            Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/v1/tickets/{ticket}/qr")).StatusCode);
            await delivery.BackfillImagesAsync(CancellationToken.None);
            await delivery.BackfillImagesAsync(CancellationToken.None);
            using var restoredResponse = await client.GetAsync($"/api/v1/tickets/{ticket}/qr");
            var restored = await BaselineDataset.Data(restoredResponse);
            Assert.Equal(original.GetProperty("qrToken").GetString(), restored.GetProperty("qrToken").GetString());
            Assert.Equal(original.GetProperty("qrImageDataUrl").GetString(), restored.GetProperty("qrImageDataUrl").GetString());
        }
        finally { await data.ClearAsync(); }
    }

    [Fact]
    public async Task UserRateLimitPartitionsByValidatedSubject()
    {
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["TrafficLimits:Enabled"] = "true", ["TrafficLimits:User:read"] = "1" })));
        using var a = host.CreateClient(); using var b = host.CreateClient();
        a.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("limit-a", "limit-a@example.test", ["Customer"]));
        b.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("limit-b", "limit-b@example.test", ["Customer"]));
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/v1/system/info")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await a.GetAsync("/api/v1/system/info")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/v1/system/info")).StatusCode);
    }

    [Fact]
    public async Task TwoApiHostsShareIdempotencyAndInventoryWithoutStickySessions()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        await using var replica = factory.WithWebHostBuilder(_ => { });
        using var first = data.Client(); using var second = replica.CreateClient();
        second.DefaultRequestHeaders.Authorization = new("Bearer", data.UserToken(1));
        try
        {
            using var created = await BaselineDataset.Post(first, "/api/v1/holds", BaselineDataset.Selection(), "replica-replay");
            using var replay = await BaselineDataset.Post(second, "/api/v1/holds", BaselineDataset.Selection(), "replica-replay");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(await created.Content.ReadAsStringAsync()),
                System.Text.Json.Nodes.JsonNode.Parse(await replay.Content.ReadAsStringAsync())));
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(i => BaselineDataset.Post(i % 2 == 0 ? first : second,
                "/api/v1/holds", BaselineDataset.Selection(), "replica-burst-" + i, token: data.UserToken(i + 1))));
            try
            {
                Assert.Equal(49, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
                Assert.Equal(51, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
                Assert.Equal(50, await data.Count($"SELECT held_quantity FROM ticket_inventories WHERE ticket_type_id='{BaselineDataset.Type}'"));
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally { await data.ClearAsync(); }
    }

    [Fact]
    public async Task SharedCatalogReadersDoNotSerializeHoldCreation()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        try
        {
            await using var reader = new NpgsqlConnection(factory.ConnectionString);
            await reader.OpenAsync();
            await using var tx = await reader.BeginTransactionAsync();
            await using var command = new NpgsqlCommand($"SELECT id FROM events WHERE id='{BaselineDataset.Event}' FOR SHARE", reader, tx);
            await command.ExecuteNonQueryAsync();
            using var client = data.Client();
            using var response = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "shared-reader");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await tx.RollbackAsync();
        }
        finally { await data.ClearAsync(); }
    }

    [Fact]
    public async Task MetadataCacheInvalidatesAcrossHostsAndAlwaysRefreshesInventory()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        await using var hostA = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["CatalogCache:Enabled"] = "true" })));
        await using var hostB = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["CatalogCache:Enabled"] = "true" })));
        using var a = hostA.CreateClient(); using var b = hostB.CreateClient();
        string path = "/api/v1/events/" + BaselineDataset.Event;
        try
        {
            using var firstA = await a.GetAsync(path); using var firstB = await b.GetAsync(path);
            Assert.Equal("MISS", firstA.Headers.GetValues("X-Catalog-Cache").Single());
            Assert.Equal("MISS", firstB.Headers.GetValues("X-Catalog-Cache").Single());
            using var buyer = data.Client();
            using var hold = await BaselineDataset.Post(buyer, "/api/v1/holds", BaselineDataset.Selection(), "cached-inventory");
            Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
            using var hit = await b.GetAsync(path);
            Assert.Equal("HIT", hit.Headers.GetValues("X-Catalog-Cache").Single());
            var body = await hit.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(49, body.GetProperty("sessions")[0].GetProperty("ticketTypes")[0].GetProperty("availableQuantity").GetInt32());
            await factory.ExecuteSqlAsync($"UPDATE events SET name='Changed across replicas' WHERE id='{BaselineDataset.Event}'");
            foreach (var client in new[] { a, b })
            {
                using var updated = await client.GetAsync(path);
                Assert.Equal("MISS", updated.Headers.GetValues("X-Catalog-Cache").Single());
                Assert.Equal("Changed across replicas", (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
            }
            await factory.ExecuteSqlAsync($"UPDATE events SET status='Cancelled' WHERE id='{BaselineDataset.Event}'");
            Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(path)).StatusCode);
        }
        finally { await data.ClearAsync(); }
    }

    [Theory]
    [InlineData("Ip")]
    [InlineData("User")]
    [InlineData("Client")]
    public async Task EachTrafficDimensionReturns429AndRetryAfter(string dimension)
    {
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TrafficLimits:Enabled"] = "true", [$"TrafficLimits:{dimension}:read"] = "2"
            })));
        using var client = host.CreateClient();
        using var first = await client.GetAsync("/api/v1/system/info");
        using var second = await client.GetAsync("/api/v1/system/info");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
        using var rejected = await client.GetAsync("/api/v1/system/info");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("RATE_LIMITED", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public void WorkerRenderedImagesAreEncryptedAndBoundToTicketIdentity()
    {
        var signer = new QrTokens();
        Guid id = Guid.NewGuid();
        string token = signer.Create(id, "nonce", signer.ActiveKeyId);
        byte[] encrypted = signer.RenderEncrypted(id, token, signer.ActiveKeyId);
        byte[] png = signer.ReadImage(id, encrypted, signer.ActiveKeyId);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.NotEqual(png, encrypted);
        Assert.ThrowsAny<CryptographicException>(() => signer.ReadImage(Guid.NewGuid(), encrypted, signer.ActiveKeyId));
        encrypted[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => signer.ReadImage(id, encrypted, signer.ActiveKeyId));
    }

    [Fact]
    public async Task WorkerPublishesQueueAndLagMetricsForAdminOnly()
    {
        await ProcessingMetrics.SampleAsync(factory.ConnectionString, factory.BrokerConfig, CancellationToken.None);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/operations/metrics")).StatusCode);
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("phase9-metrics-admin", "phase9-metrics@example.test", ["Admin"]));
        using var response = await admin.GetAsync("/api/v1/operations/metrics");
        response.EnsureSuccessStatusCode();
        var metrics = await response.Content.ReadFromJsonAsync<JsonElement>();
        var worker = metrics.GetProperty("workers")[0];
        Assert.False(worker.GetProperty("stale").GetBoolean());
        Assert.True(worker.GetProperty("values_json").GetProperty("readyMessages").TryGetProperty(RabbitDelivery.Queue, out _));
        Assert.True(worker.GetProperty("values_json").GetProperty("consumerLagSeconds").GetDecimal() >= 0);
    }
}
