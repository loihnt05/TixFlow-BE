using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TixFlow.Application.WaitingRoom;
using TixFlow.Infrastructure.WaitingRoom;

namespace TixFlow.Api.Tests;

[Collection("Integration")]
public sealed class WaitingRoomResilienceTests(AuthApiFactory factory)
{
    private static readonly Guid SessionId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid TicketTypeId = Guid.Parse("50000000-0000-0000-0000-000000000012");

    [Fact]
    public async Task TwoApiHostsShareFifoBudgetAndAdmissionToken()
    {
        using WebApplicationFactory<Program> replica = factory.WithWebHostBuilder(_ => { });
        IWaitingRoomStore first = factory.Services.GetRequiredService<IWaitingRoomStore>();
        IWaitingRoomStore second = replica.Services.GetRequiredService<IWaitingRoomStore>();
        Assert.NotSame(first, second);

        Guid syntheticSale = Guid.NewGuid();
        QueueEntrySnapshot[] joined = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(index => (index % 2 == 0 ? first : second)
                .JoinAsync(syntheticSale, $"replica-{index}", CancellationToken.None)));
        Assert.Equal(40, joined.Select(entry => entry.QueueId).Distinct().Count());
        QueueEntrySnapshot?[] grants = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(index => (index % 2 == 0 ? second : first)
                .GrantNextAsync(syntheticSale, 5, CancellationToken.None)));
        QueueEntrySnapshot[] issued = grants.OfType<QueueEntrySnapshot>().ToArray();
        Assert.NotEmpty(issued);
        Assert.Equal(issued.Length, issued.Select(entry => entry.QueueId).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, issued.Length).Select(index => (long)index),
            issued.Select(entry => entry.Sequence).Order());
        Assert.All(issued.GroupBy(entry => entry.ExpiresAtUnixMs / 1000),
            bucket => Assert.True(bucket.Count() <= 5));
        Assert.Equal(joined.First(entry => entry.Sequence == 1).QueueId,
            issued.First(entry => entry.Sequence == 1).QueueId);

        string suffix = Guid.NewGuid().ToString("N");
        string subject = $"cross-host-{suffix}";
        using HttpClient clientA = Client(factory.CreateClient(), subject);
        using HttpClient clientB = Client(replica.CreateClient(), subject);
        Guid? holdId = null;
        long holdVersion = 0;
        await factory.ExecuteSqlAsync($"""
            INSERT INTO waiting_room_policies(event_session_id, state, max_grants_per_second)
            VALUES ('{SessionId}', 'Active', 5)
            ON CONFLICT (event_session_id) DO UPDATE SET state = 'Active';
            """);
        try
        {
            using HttpResponseMessage join = await clientA.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            Assert.Equal(HttpStatusCode.OK, join.StatusCode);
            JsonElement joinData = (await Read(join)).GetProperty("data");
            Guid queueId = joinData.GetProperty("queueId").GetGuid();
            int pollAfterMs = joinData.GetProperty("pollAfterMs").GetInt32();
            Assert.InRange(pollAfterMs, 3000, 5000);
            string? token = null;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                using HttpResponseMessage status = await clientB.GetAsync($"/api/v1/waiting-room/status?queueId={queueId}");
                JsonElement data = (await Read(status)).GetProperty("data");
                Assert.Equal(pollAfterMs, data.GetProperty("pollAfterMs").GetInt32());
                if (data.GetProperty("status").GetString() == "Admitted")
                {
                    token = data.GetProperty("admissionToken").GetString();
                    break;
                }
                await Task.Delay(100);
            }
            Assert.False(string.IsNullOrWhiteSpace(token));
            using HttpResponseMessage created = await PostHold(clientA, $"cross-host-{suffix}", token);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            JsonElement hold = (await Read(created)).GetProperty("data");
            holdId = hold.GetProperty("id").GetGuid();
            holdVersion = hold.GetProperty("version").GetInt64();
        }
        finally
        {
            await factory.ExecuteSqlAsync($"DELETE FROM waiting_room_policies WHERE event_session_id = '{SessionId}';");
            if (holdId.HasValue)
            {
                using var cancel = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/holds/{holdId}/cancellations")
                    { Content = JsonContent.Create(new { reason = "Cross-host test cleanup" }) };
                cancel.Headers.Add("Idempotency-Key", $"cancel-{suffix}");
                cancel.Headers.Add("X-Expected-Version", holdVersion.ToString());
                using HttpResponseMessage cancelled = await clientA.SendAsync(cancel);
                Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            }
        }
    }

    [Fact]
    public async Task RedisRestartPreservesQueueAndFifo()
    {
        await using IContainer redis = new ContainerBuilder()
            .WithImage("redis:7.4-alpine")
            .WithCommand("redis-server", "--appendonly", "yes")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379))
            .Build();
        await redis.StartAsync();
        string endpoint = $"{redis.Hostname}:{redis.GetMappedPublicPort(6379)}";
        var options = ConfigurationOptions.Parse(endpoint);
        options.AbortOnConnectFail = false;
        options.AsyncTimeout = 1000;
        using (IConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(options))
        {
            var store = new RedisWaitingRoomStore(connection);
            Guid sale = Guid.NewGuid();
            QueueEntrySnapshot first = await store.JoinAsync(sale, "restart-first", CancellationToken.None);
            QueueEntrySnapshot second = await store.JoinAsync(sale, "restart-second", CancellationToken.None);
            await redis.StopAsync();
            await redis.StartAsync();
            var recoveredOptions = ConfigurationOptions.Parse($"{redis.Hostname}:{redis.GetMappedPublicPort(6379)}");
            recoveredOptions.AbortOnConnectFail = true;
            recoveredOptions.ConnectTimeout = 5000;
            using IConnectionMultiplexer recovered = await ConnectionMultiplexer.ConnectAsync(recoveredOptions);
            Assert.True(await recovered.GetDatabase().PingAsync() < TimeSpan.FromSeconds(5));
            var afterRestart = new RedisWaitingRoomStore(recovered);
            Assert.Equal(first.QueueId, (await afterRestart.JoinAsync(sale, "restart-first", CancellationToken.None)).QueueId);
            Assert.Equal(first.QueueId, (await afterRestart.GrantNextAsync(sale, 5, CancellationToken.None))?.QueueId);
            Assert.Equal(second.QueueId, (await afterRestart.GrantNextAsync(sale, 5, CancellationToken.None))?.QueueId);
        }
    }

    [Fact]
    public async Task ControllerReplicaRestartDrainsTheSameQueueWithinBudget()
    {
        const int count = 12;
        Guid sale = Guid.NewGuid();
        await factory.ExecuteSqlAsync($"""
            INSERT INTO event_sessions(id, event_id, name, starts_at_utc, ends_at_utc, status)
            SELECT '{sale}', event_id, 'Waiting room restart {sale:N}',
                starts_at_utc + interval '100 days', ends_at_utc + interval '100 days', 'OnSale'
            FROM event_sessions WHERE id = '{SessionId}';
            INSERT INTO waiting_room_policies(event_session_id, state, max_grants_per_second)
            VALUES ('{sale}', 'Paused', 2);
            """);
        try
        {
            var firstReplica = factory.WithWebHostBuilder(_ => { });
            QueueEntrySnapshot[] joined;
            string[] subjects = Enumerable.Range(0, count)
                .Select(index => $"restart-controller-{Guid.NewGuid():N}-{index}").ToArray();
            try
            {
                IWaitingRoomStore store = firstReplica.Services.GetRequiredService<IWaitingRoomStore>();
                joined = await Task.WhenAll(subjects
                    .Select(subject => store.JoinAsync(sale, subject, CancellationToken.None)));
                Assert.Equal(count, joined.Select(item => item.QueueId).Distinct().Count());
                int firstIndex = Array.IndexOf(joined, joined.MinBy(entry => entry.Sequence)!);
                await factory.ExecuteSqlAsync($"UPDATE waiting_room_policies SET state = 'Active' WHERE event_session_id = '{sale}';");
                bool firstGrant = false;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    firstGrant = (await store.GetAsync(joined[firstIndex].QueueId, subjects[firstIndex], CancellationToken.None))?.Status == "Admitted";
                    if (firstGrant) break;
                    await Task.Delay(100);
                }
                Assert.True(firstGrant);
            }
            finally { firstReplica.Dispose(); }

            using WebApplicationFactory<Program> replacement = factory.WithWebHostBuilder(_ => { });
            IWaitingRoomStore recoveredStore = replacement.Services.GetRequiredService<IWaitingRoomStore>();
            var admitted = new List<QueueEntrySnapshot>();
            for (int attempt = 0; attempt < 100; attempt++)
            {
                admitted.Clear();
                for (int index = 0; index < joined.Length; index++)
                {
                    QueueEntrySnapshot? current = await recoveredStore.GetAsync(
                        joined[index].QueueId, subjects[index], CancellationToken.None);
                    if (current?.Status == "Admitted") admitted.Add(current);
                }
                if (admitted.Count == count) break;
                await Task.Delay(100);
            }
            Assert.Equal(count, admitted.Count);
            Assert.Equal(count, admitted.Select(item => item.GrantId).Distinct().Count());
            Assert.All(admitted.GroupBy(item => item.ExpiresAtUnixMs / 1000),
                bucket => Assert.True(bucket.Count() <= 2));
        }
        finally { await factory.ExecuteSqlAsync($"DELETE FROM event_sessions WHERE id = '{sale}';"); }
    }

    [Fact]
    public async Task ActiveGateFailsClosedWhenItsRedisEndpointIsUnavailable()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string subject = $"redis-outage-{suffix}";
        using HttpClient healthy = Client(factory.CreateClient(), subject);
        await factory.ExecuteSqlAsync($"""
            INSERT INTO waiting_room_policies(event_session_id, state, max_grants_per_second)
            VALUES ('{SessionId}', 'Active', 5)
            ON CONFLICT (event_session_id) DO UPDATE SET state = 'Active';
            """);
        try
        {
            using HttpResponseMessage joined = await healthy.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            Guid queueId = (await Read(joined)).GetProperty("data").GetProperty("queueId").GetGuid();
            string? token = null;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                using HttpResponseMessage status = await healthy.GetAsync($"/api/v1/waiting-room/status?queueId={queueId}");
                JsonElement data = (await Read(status)).GetProperty("data");
                if (data.GetProperty("status").GetString() == "Admitted")
                {
                    token = data.GetProperty("admissionToken").GetString();
                    break;
                }
                await Task.Delay(100);
            }
            Assert.False(string.IsNullOrWhiteSpace(token));
            using WebApplicationFactory<Program> disconnected = factory.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" })));
            Assert.Equal("127.0.0.1:1", disconnected.Services.GetRequiredService<IConfiguration>()
                .GetConnectionString("Redis"));
            using HttpClient failing = Client(disconnected.CreateClient(), subject);
            using HttpResponseMessage response = await PostHold(failing, $"outage-{suffix}", token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("WAITING_ROOM_UNAVAILABLE", (await Read(response)).GetProperty("code").GetString());
            Assert.Equal(TimeSpan.FromSeconds(3), response.Headers.RetryAfter?.Delta);
        }
        finally { await factory.ExecuteSqlAsync($"DELETE FROM waiting_room_policies WHERE event_session_id = '{SessionId}';"); }
    }

    [Fact]
    public async Task ManagementCanPauseButCannotActivateWhileRedisIsUnavailable()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using WebApplicationFactory<Program> disconnected = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" })));
        using HttpClient admin = disconnected.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Token($"outage-admin-{suffix}", $"outage-admin-{suffix}@example.test", ["Admin"]));
        string path = $"/api/v1/management/event-sessions/{SessionId}/waiting-room-policy";
        try
        {
            using HttpResponseMessage paused = await admin.PutAsJsonAsync(path,
                new { state = "Paused", maxGrantsPerSecond = 5, expectedVersion = 0,
                    reason = "Pause while Redis is unavailable" });
            Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
            JsonElement data = (await Read(paused)).GetProperty("data");
            Assert.Equal("Paused", data.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("queueDepth").ValueKind);

            using HttpResponseMessage active = await admin.PutAsJsonAsync(path,
                new { state = "Active", maxGrantsPerSecond = 5, expectedVersion = 1,
                    reason = "Attempt to resume without Redis" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, active.StatusCode);
            using HttpResponseMessage current = await admin.GetAsync(path);
            Assert.Equal("Paused", (await Read(current)).GetProperty("data").GetProperty("state").GetString());
        }
        finally { await factory.ExecuteSqlAsync($"DELETE FROM waiting_room_policies WHERE event_session_id = '{SessionId}';"); }
    }

    private HttpClient Client(HttpClient client, string subject)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Token(subject, $"{subject}@example.test", ["Customer"]));
        return client;
    }

    private static async Task<HttpResponseMessage> PostHold(HttpClient client, string key, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/holds")
        {
            Content = JsonContent.Create(new { eventSessionId = SessionId, items = new[]
                { new { ticketTypeId = TicketTypeId, inventoryMode = "GeneralAdmission", quantity = 1 } } })
        };
        request.Headers.Add("Idempotency-Key", key);
        if (token is not null) request.Headers.Add("X-Admission-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
