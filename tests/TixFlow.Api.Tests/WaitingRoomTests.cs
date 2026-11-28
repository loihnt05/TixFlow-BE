using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TixFlow.Application.WaitingRoom;
using StackExchange.Redis;
using Microsoft.EntityFrameworkCore;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Tests;

[Collection("Integration")]
public sealed class WaitingRoomTests(AuthApiFactory factory)
{
    private static readonly Guid SessionId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid FlashTicketTypeId = Guid.Parse("50000000-0000-0000-0000-000000000012");

    [Fact]
    public async Task RuntimeSwaggerDocumentsWaitingRoomResponsesAndAdmissionHeader()
    {
        using HttpClient client = factory.CreateClient();
        JsonElement spec = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        JsonElement paths = spec.GetProperty("paths");
        JsonElement join = paths.GetProperty("/api/v1/waiting-room/join").GetProperty("post");
        Assert.True(join.GetProperty("responses").TryGetProperty("503", out _));
        Assert.True(join.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").TryGetProperty("$ref", out _));
        JsonElement status = paths.GetProperty("/api/v1/waiting-room/status").GetProperty("get");
        Assert.True(status.GetProperty("responses").TryGetProperty("404", out _));
        JsonElement abandon = paths.GetProperty("/api/v1/waiting-room/{queueId}").GetProperty("delete");
        Assert.True(abandon.GetProperty("responses").TryGetProperty("204", out _));
        JsonElement hold = paths.GetProperty("/api/v1/holds").GetProperty("post");
        Assert.Contains(hold.GetProperty("parameters").EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == "Idempotency-Key"
            && parameter.GetProperty("required").GetBoolean());
        Assert.Contains(hold.GetProperty("parameters").EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == "X-Admission-Token");
        Assert.True(hold.GetProperty("responses").TryGetProperty("503", out _));
    }

    [Fact]
    public async Task ConcurrentJoinIsDeduplicatedAndGrantsFollowFifo()
    {
        IWaitingRoomStore store = factory.Services.GetRequiredService<IWaitingRoomStore>();
        Guid sale = Guid.NewGuid();
        QueueEntrySnapshot[] sameUser = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(_ => store.JoinAsync(sale, "same-customer", CancellationToken.None)));
        Assert.Single(sameUser.Select(entry => entry.QueueId).Distinct());
        Assert.All(sameUser, entry => Assert.Equal(1, entry.Sequence));

        QueueEntrySnapshot[] others = new QueueEntrySnapshot[25];
        for (int index = 0; index < others.Length; index++)
            others[index] = await store.JoinAsync(sale, $"customer-{index}", CancellationToken.None);
        Assert.Equal(Enumerable.Range(2, 25).Select(index => (long)index),
            others.Select(entry => entry.Sequence));

        var granted = new List<QueueEntrySnapshot>();
        for (int index = 0; index < 26; index++)
            granted.Add((await store.GrantNextAsync(sale, 1000, CancellationToken.None))!);
        Assert.Equal(Enumerable.Range(1, 26).Select(index => (long)index),
            granted.Select(entry => entry.Sequence));
        Assert.All(granted, entry => Assert.Equal("Admitted", entry.Status));
        Assert.Equal(26, granted.Select(entry => entry.GrantId).Distinct().Count());
        Assert.Null(await store.GrantNextAsync(sale, 1000, CancellationToken.None));

        QueueEntrySnapshot? ownerView = await store.GetAsync(sameUser[0].QueueId, "same-customer", CancellationToken.None);
        Assert.Equal("Admitted", ownerView?.Status);
        Assert.Null(await store.GetAsync(sameUser[0].QueueId, "other-customer", CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentAdmissionCapacityReleasesOnConsumeAndAbandon()
    {
        IWaitingRoomStore store = factory.Services.GetRequiredService<IWaitingRoomStore>();
        Guid sale = Guid.NewGuid();
        QueueEntrySnapshot[] entries = new QueueEntrySnapshot[12];
        for (int index = 0; index < entries.Length; index++)
            entries[index] = await store.JoinAsync(sale, $"capacity-{index}", CancellationToken.None);

        QueueEntrySnapshot?[] concurrent = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => store.GrantNextAsync(sale, 1000, CancellationToken.None, 2)));
        QueueEntrySnapshot[] issued = concurrent.OfType<QueueEntrySnapshot>().ToArray();
        Assert.Equal(2, issued.Length);
        Assert.Equal(new long[] { 1, 2 }, issued.Select(entry => entry.Sequence).Order());
        Assert.Equal(2, await factory.Services.GetRequiredService<IConnectionMultiplexer>()
            .GetDatabase().SortedSetLengthAsync($"wr:active:{sale:D}"));

        QueueEntrySnapshot first = issued.Single(entry => entry.Sequence == 1);
        Assert.Equal("OK", await store.BindIntentAsync(first.QueueId, sale, first.GrantId!.Value,
            "capacity-0", "key", "payload", CancellationToken.None));
        Assert.Equal("OK", await store.BindIntentAsync(first.QueueId, sale, first.GrantId.Value,
            "capacity-0", "key", "payload", CancellationToken.None));
        QueueEntrySnapshot? third = await store.GrantNextAsync(sale, 1000, CancellationToken.None, 2);
        Assert.Equal(3, third?.Sequence);

        QueueEntrySnapshot second = issued.Single(entry => entry.Sequence == 2);
        Assert.True(await store.AbandonAsync(second.QueueId, "capacity-1", CancellationToken.None));
        QueueEntrySnapshot? fourth = await store.GrantNextAsync(sale, 1000, CancellationToken.None, 2);
        Assert.Equal(4, fourth?.Sequence);

        IConnectionMultiplexer redis = factory.Services.GetRequiredService<IConnectionMultiplexer>();
        await redis.GetDatabase().HashSetAsync($"wr:entry:{third!.QueueId:D}", "expiresAt", 0);
        await redis.GetDatabase().SortedSetAddAsync($"wr:active:{sale:D}", third.QueueId.ToString("D"), 0);
        QueueEntrySnapshot? fifth = await store.GrantNextAsync(sale, 1000, CancellationToken.None, 2);
        Assert.Equal(5, fifth?.Sequence);
        Assert.Equal("Expired", (await store.GetAsync(third.QueueId, "capacity-2", CancellationToken.None))?.Status);
    }

    [Fact]
    public async Task AbandonRemovesTheEntryWithoutMovingOtherCustomers()
    {
        IWaitingRoomStore store = factory.Services.GetRequiredService<IWaitingRoomStore>();
        Guid sale = Guid.NewGuid();
        QueueEntrySnapshot first = await store.JoinAsync(sale, "leaving", CancellationToken.None);
        QueueEntrySnapshot second = await store.JoinAsync(sale, "staying", CancellationToken.None);
        Assert.True(await store.AbandonAsync(first.QueueId, "leaving", CancellationToken.None));
        Assert.Equal("Abandoned", (await store.GetAsync(first.QueueId, "leaving", CancellationToken.None))?.Status);
        Assert.Equal(1, (await store.GetAsync(second.QueueId, "staying", CancellationToken.None))?.PositionEstimate);
        Assert.Equal(second.QueueId, (await store.GrantNextAsync(sale, 1000, CancellationToken.None))?.QueueId);
        Assert.False(await store.AbandonAsync(first.QueueId, "not-owner", CancellationToken.None));
        QueueEntrySnapshot rejoined = await store.JoinAsync(sale, "leaving", CancellationToken.None);
        Assert.NotEqual(first.QueueId, rejoined.QueueId);
        Assert.True(rejoined.Sequence > second.Sequence);
    }

    [Fact]
    public async Task ExpiredHeadIsSkippedAndConcurrentGrantsAreUnique()
    {
        IWaitingRoomStore store = factory.Services.GetRequiredService<IWaitingRoomStore>();
        IConnectionMultiplexer redis = factory.Services.GetRequiredService<IConnectionMultiplexer>();
        Guid sale = Guid.NewGuid();
        QueueEntrySnapshot expired = await store.JoinAsync(sale, "expired", CancellationToken.None);
        QueueEntrySnapshot[] live = new QueueEntrySnapshot[20];
        for (int index = 0; index < live.Length; index++)
            live[index] = await store.JoinAsync(sale, $"live-{index}", CancellationToken.None);
        await redis.GetDatabase().HashSetAsync($"wr:entry:{expired.QueueId:D}", "expiresAt", 0);

        QueueEntrySnapshot?[] grants = await Task.WhenAll(Enumerable.Range(0, live.Length)
            .Select(_ => store.GrantNextAsync(sale, 1000, CancellationToken.None)));
        Assert.DoesNotContain(grants, entry => entry is null);
        Assert.Equal(live.Select(entry => entry.QueueId).Order(),
            grants.Select(entry => entry!.QueueId).Order());
        Assert.Equal(live.Length, grants.Select(entry => entry!.GrantId).Distinct().Count());
        Assert.Equal("Expired", (await store.GetAsync(expired.QueueId, "expired", CancellationToken.None))?.Status);
        Assert.Null(await store.GrantNextAsync(sale, 1000, CancellationToken.None));
    }

    [Fact]
    public async Task GrantSkipsMoreThanOneBatchOfExpiredEntries()
    {
        IWaitingRoomStore store = factory.Services.GetRequiredService<IWaitingRoomStore>();
        IConnectionMultiplexer redis = factory.Services.GetRequiredService<IConnectionMultiplexer>();
        Guid sale = Guid.NewGuid();
        var expired = new List<QueueEntrySnapshot>();
        for (int index = 0; index < 101; index++)
            expired.Add(await store.JoinAsync(sale, $"stale-{index}", CancellationToken.None));
        QueueEntrySnapshot live = await store.JoinAsync(sale, "live", CancellationToken.None);
        foreach (QueueEntrySnapshot entry in expired)
            await redis.GetDatabase().HashSetAsync($"wr:entry:{entry.QueueId:D}", "expiresAt", 0);

        QueueEntrySnapshot? granted = await store.GrantNextAsync(sale, 1000, CancellationToken.None);
        Assert.Equal(live.QueueId, granted?.QueueId);
        Assert.Equal("Admitted", granted?.Status);
    }

    [Fact]
    public async Task ConcurrentControllersShareThePerSecondGrantBudget()
    {
        IWaitingRoomStore store = factory.Services.GetRequiredService<IWaitingRoomStore>();
        Guid sale = Guid.NewGuid();
        for (int index = 0; index < 20; index++)
            await store.JoinAsync(sale, $"budget-{index}", CancellationToken.None);

        QueueEntrySnapshot?[] grants = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => store.GrantNextAsync(sale, 2, CancellationToken.None)));
        Assert.Contains(grants, entry => entry is not null);
        Assert.All(grants.Where(entry => entry is not null)
                .GroupBy(entry => entry!.ExpiresAtUnixMs / 1000),
            second => Assert.True(second.Count() <= 2));
    }

    [Fact]
    public async Task PausedPolicyRetainsGateWithoutGranting()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using HttpClient customer = Client($"paused-{suffix}");
        await factory.ExecuteSqlAsync($"""
            INSERT INTO waiting_room_policies(event_session_id, state, max_grants_per_second)
            VALUES ('{SessionId}', 'Paused', 5)
            ON CONFLICT (event_session_id) DO UPDATE SET state = 'Paused';
            """);
        try
        {
            using HttpResponseMessage joined = await customer.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            Guid queueId = (await Read(joined)).GetProperty("data").GetProperty("queueId").GetGuid();
            await Task.Delay(600);
            using HttpResponseMessage status = await customer.GetAsync($"/api/v1/waiting-room/status?queueId={queueId}");
            Assert.Equal("Waiting", (await Read(status)).GetProperty("data").GetProperty("status").GetString());
            object body = new { eventSessionId = SessionId, items = new[]
                { new { ticketTypeId = FlashTicketTypeId, inventoryMode = "GeneralAdmission", quantity = 1 } } };
            using HttpResponseMessage hold = await PostHold(customer, body, "paused-key", null);
            Assert.Equal(HttpStatusCode.Conflict, hold.StatusCode);
            Assert.Equal("WAITING_ROOM_REQUIRED", (await Read(hold)).GetProperty("code").GetString());
        }
        finally { await factory.ExecuteSqlAsync($"DELETE FROM waiting_room_policies WHERE event_session_id = '{SessionId}';"); }
    }

    [Fact]
    public async Task SoldOutSessionRejectsExistingQueueEntryAndNewJoin()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using HttpClient customer = Client($"sold-out-{suffix}");
        await factory.ExecuteSqlAsync($"""
            INSERT INTO waiting_room_policies(event_session_id, state, max_grants_per_second)
            VALUES ('{SessionId}', 'Paused', 5)
            ON CONFLICT (event_session_id) DO UPDATE SET state = 'Paused';
            """);
        try
        {
            using HttpResponseMessage joined = await customer.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            Guid queueId = (await Read(joined)).GetProperty("data").GetProperty("queueId").GetGuid();
            using HttpResponseMessage beforeClose = await customer.GetAsync($"/api/v1/waiting-room/status?queueId={queueId}");
            Assert.Equal("Waiting", (await Read(beforeClose)).GetProperty("data").GetProperty("status").GetString());
            await factory.ExecuteSqlAsync($"UPDATE event_sessions SET status = 'SoldOut' WHERE id = '{SessionId}';");
            await Task.Delay(150);

            using HttpResponseMessage status = await customer.GetAsync($"/api/v1/waiting-room/status?queueId={queueId}");
            JsonElement data = (await Read(status)).GetProperty("data");
            Assert.Equal("Rejected", data.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("admissionToken").ValueKind);
            using HttpResponseMessage newJoin = await customer.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            Assert.Equal(HttpStatusCode.Conflict, newJoin.StatusCode);
        }
        finally
        {
            await factory.ExecuteSqlAsync($"UPDATE event_sessions SET status = 'OnSale' WHERE id = '{SessionId}';");
            await factory.ExecuteSqlAsync($"DELETE FROM waiting_room_policies WHERE event_session_id = '{SessionId}';");
        }
    }

    [Fact]
    public async Task ApiIsDirectByDefaultAndEnforcesOwnerWhenPolicyIsActive()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using HttpClient customer = Client($"queue-owner-{suffix}");
        using HttpClient stranger = Client($"queue-stranger-{suffix}");
        using HttpResponseMessage directResponse = await customer.PostAsJsonAsync("/api/v1/waiting-room/join",
            new { eventSessionId = SessionId });
        Assert.Equal(HttpStatusCode.OK, directResponse.StatusCode);
        using (JsonDocument direct = await JsonDocument.ParseAsync(await directResponse.Content.ReadAsStreamAsync()))
            Assert.Equal("Direct", direct.RootElement.GetProperty("data").GetProperty("mode").GetString());

        await factory.ExecuteSqlAsync($"""
            INSERT INTO waiting_room_policies(event_session_id, state, max_grants_per_second)
            VALUES ('{SessionId}', 'Active', 5)
            ON CONFLICT (event_session_id) DO UPDATE SET state = 'Active';
            """);
        try
        {
            using HttpResponseMessage joinedResponse = await customer.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            Assert.Equal(HttpStatusCode.OK, joinedResponse.StatusCode);
            using JsonDocument joined = await JsonDocument.ParseAsync(await joinedResponse.Content.ReadAsStreamAsync());
            JsonElement data = joined.RootElement.GetProperty("data");
            Assert.Equal("Waiting", data.GetProperty("mode").GetString());
            Guid queueId = data.GetProperty("queueId").GetGuid();
            Assert.Equal(1, data.GetProperty("positionEstimate").GetInt64());

            using HttpResponseMessage repeated = await customer.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            using JsonDocument repeatedBody = await JsonDocument.ParseAsync(await repeated.Content.ReadAsStreamAsync());
            Assert.Equal(queueId, repeatedBody.RootElement.GetProperty("data").GetProperty("queueId").GetGuid());

            Assert.Equal(HttpStatusCode.NotFound,
                (await stranger.GetAsync($"/api/v1/waiting-room/status?queueId={queueId}")).StatusCode);
            using HttpResponseMessage status = await customer.GetAsync($"/api/v1/waiting-room/status?queueId={queueId}");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.Equal("no-store", status.Headers.CacheControl?.ToString());
            Assert.Equal(HttpStatusCode.NoContent,
                (await customer.DeleteAsync($"/api/v1/waiting-room/{queueId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await stranger.DeleteAsync($"/api/v1/waiting-room/{queueId}")).StatusCode);
        }
        finally
        {
            await factory.ExecuteSqlAsync($"DELETE FROM waiting_room_policies WHERE event_session_id = '{SessionId}';");
        }
    }

    [Fact]
    public async Task ControllerAdmissionGatesHoldAndAllowsOnlySameIntentReplay()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using HttpClient customer = Client($"gate-owner-{suffix}");
        using HttpClient stranger = Client($"gate-stranger-{suffix}");
        Guid? holdId = null;
        long holdVersion = 0;
        await factory.ExecuteSqlAsync($"""
            INSERT INTO waiting_room_policies(event_session_id, state, max_grants_per_second)
            VALUES ('{SessionId}', 'Active', 5)
            ON CONFLICT (event_session_id) DO UPDATE SET state = 'Active';
            """);
        try
        {
            object body = new { eventSessionId = SessionId, items = new[]
                { new { ticketTypeId = FlashTicketTypeId, inventoryMode = "GeneralAdmission", quantity = 1 } } };
            using HttpResponseMessage missing = await PostHold(customer, body, "gate-key", null);
            Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
            Assert.Equal("WAITING_ROOM_REQUIRED", (await Read(missing)).GetProperty("code").GetString());

            using HttpResponseMessage joined = await customer.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
            JsonElement joinData = (await Read(joined)).GetProperty("data");
            Guid queueId = joinData.GetProperty("queueId").GetGuid();
            int pollAfter = joinData.GetProperty("pollAfterMs").GetInt32();
            Assert.InRange(pollAfter, 3000, 5000);
            string? token = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                using HttpResponseMessage status = await customer.GetAsync($"/api/v1/waiting-room/status?queueId={queueId}");
                JsonElement data = (await Read(status)).GetProperty("data");
                Assert.Equal(pollAfter, data.GetProperty("pollAfterMs").GetInt32());
                if (data.GetProperty("status").GetString() == "Admitted")
                {
                    token = data.GetProperty("admissionToken").GetString();
                    break;
                }
                await Task.Delay(100);
            }
            Assert.False(string.IsNullOrEmpty(token));

            using HttpResponseMessage stolen = await PostHold(stranger, body, "stolen-key", token);
            Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);
            Assert.Equal("ADMISSION_INVALID", (await Read(stolen)).GetProperty("code").GetString());

            using HttpResponseMessage created = await PostHold(customer, body, "gate-key", token);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            JsonElement hold = (await Read(created)).GetProperty("data");
            holdId = hold.GetProperty("id").GetGuid();
            holdVersion = hold.GetProperty("version").GetInt64();

            using HttpResponseMessage conflict = await PostHold(customer, body, "other-key", token);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("ADMISSION_INTENT_CONFLICT", (await Read(conflict)).GetProperty("code").GetString());

            using HttpResponseMessage replay = await PostHold(customer, body, "gate-key", null);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal(holdId, (await Read(replay)).GetProperty("data").GetProperty("id").GetGuid());

            await factory.ExecuteSqlAsync($"UPDATE waiting_room_policies SET state = 'Closed' WHERE event_session_id = '{SessionId}';");
            using HttpResponseMessage closedReplay = await PostHold(customer, body, "gate-key", null);
            Assert.Equal(HttpStatusCode.Created, closedReplay.StatusCode);
            Assert.Equal("true", closedReplay.Headers.GetValues("Idempotency-Replayed").Single());
            using HttpResponseMessage closedNew = await PostHold(customer, body, "closed-key", token);
            Assert.Equal("WAITING_ROOM_CLOSED", (await Read(closedNew)).GetProperty("code").GetString());
        }
        finally
        {
            await factory.ExecuteSqlAsync($"DELETE FROM waiting_room_policies WHERE event_session_id = '{SessionId}';");
            if (holdId.HasValue)
            {
                using var cancel = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/holds/{holdId}/cancellations")
                    { Content = JsonContent.Create(new { reason = "Waiting room test cleanup" }) };
                cancel.Headers.Add("Idempotency-Key", $"cancel-{suffix}");
                cancel.Headers.Add("X-Expected-Version", holdVersion.ToString());
                using HttpResponseMessage cancelled = await customer.SendAsync(cancel);
                Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            }
        }
    }

    [Fact]
    public async Task ManagementRequiresOwnershipAndAuditsVersionedPolicyChanges()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using HttpClient admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Token($"policy-admin-{suffix}", $"policy-admin-{suffix}@example.test", ["Admin"]));
        using HttpClient customer = Client($"policy-customer-{suffix}");
        string path = $"/api/v1/management/event-sessions/{SessionId}/waiting-room-policy";
        try
        {
            using HttpResponseMessage denied = await customer.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using HttpResponseMessage initial = await admin.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
            Assert.Equal(0, (await Read(initial)).GetProperty("data").GetProperty("version").GetInt32());

            using HttpResponseMessage active = await admin.PutAsJsonAsync(path,
                new { state = "Active", maxGrantsPerSecond = 5, maxConcurrentAdmissions = 2, expectedVersion = 0,
                    reason = "Open controlled sale" });
            Assert.True(active.StatusCode == HttpStatusCode.OK, await active.Content.ReadAsStringAsync());
            Assert.Equal(1, (await Read(active)).GetProperty("data").GetProperty("version").GetInt32());
            Assert.Equal(2, (await Read(active)).GetProperty("data").GetProperty("maxConcurrentAdmissions").GetInt32());
            using HttpResponseMessage queued = await customer.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
            Guid queueId = (await Read(queued)).GetProperty("data").GetProperty("queueId").GetGuid();

            using HttpResponseMessage stale = await admin.PutAsJsonAsync(path,
                new { state = "Paused", maxGrantsPerSecond = 5, expectedVersion = 0,
                    reason = "Pause for operator check" });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("POLICY_VERSION_CONFLICT", (await Read(stale)).GetProperty("code").GetString());

            using HttpResponseMessage paused = await admin.PutAsJsonAsync(path,
                new { state = "Paused", maxGrantsPerSecond = 3, expectedVersion = 1,
                    reason = "Pause for operator check" });
            Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
            Assert.Equal(2, (await Read(paused)).GetProperty("data").GetProperty("version").GetInt32());
            Assert.Equal(2, (await Read(paused)).GetProperty("data").GetProperty("maxConcurrentAdmissions").GetInt32());

            using HttpResponseMessage closed = await admin.PutAsJsonAsync(path,
                new { state = "Closed", maxGrantsPerSecond = 3, expectedVersion = 2,
                    reason = "Close after allocation" });
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            using HttpResponseMessage join = await customer.PostAsJsonAsync("/api/v1/waiting-room/join",
                new { eventSessionId = SessionId });
            Assert.Equal(HttpStatusCode.Conflict, join.StatusCode);
            await Task.Delay(150);
            using HttpResponseMessage rejected = await customer.GetAsync($"/api/v1/waiting-room/status?queueId={queueId}");
            Assert.Equal("Rejected", (await Read(rejected)).GetProperty("data").GetProperty("status").GetString());

            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            TixFlowDbContext db = scope.ServiceProvider.GetRequiredService<TixFlowDbContext>();
            Assert.Equal(3, await db.AuditLogs.CountAsync(log => log.EntityType == "WaitingRoomPolicy"
                && log.EntityId == SessionId.ToString("D") && log.Action == "WaitingRoomPolicyChanged"));
        }
        finally { await factory.ExecuteSqlAsync($"DELETE FROM waiting_room_policies WHERE event_session_id = '{SessionId}';"); }
    }

    private static async Task<HttpResponseMessage> PostHold(HttpClient client, object body, string key, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/holds")
            { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        if (token is not null) request.Headers.Add("X-Admission-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private HttpClient Client(string subject)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Token(subject, $"{subject}@example.test", ["Customer"]));
        return client;
    }
}
