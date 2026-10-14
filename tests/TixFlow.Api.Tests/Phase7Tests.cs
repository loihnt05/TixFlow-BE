using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RabbitMQ.Client;
using TixFlow.Infrastructure.Processing;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Tests;

public sealed class Phase7Tests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static readonly Guid Session = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid Type = Guid.Parse("50000000-0000-0000-0000-000000000012");
    private static readonly IConfiguration BrokerConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["RabbitMq:Host"] = "localhost", ["RabbitMq:Port"] = "15673",
        ["RabbitMq:Username"] = "tixflow_test", ["RabbitMq:Password"] = "isolated_test_only"
    }).Build();

    [Fact]
    public async Task HostedWorkerRunsExpiryOutboxIssuanceAndNotificationAcrossRestart()
    {
        await Execute("UPDATE outbox_messages SET processed_at_utc = now() WHERE type <> 'MockPaymentRequested'", Guid.Empty);
        using (var topology = new RabbitDelivery(BrokerConfig)) { }
        using (var connection = TestConnection())
        using (var channel = connection.CreateModel()) { channel.QueuePurge(RabbitDelivery.Queue); }
        await using var unpaid = await Book(paid: false);
        await using var paid = await Book(paid: true);
        await Execute("UPDATE holds SET expires_at_utc = now() - interval '1 second' WHERE id = @id", unpaid.Hold);
        var config = new ConfigurationBuilder().AddConfiguration(BrokerConfig).AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Postgres"] = factory.ConnectionString }).Build();
        using (var worker = new TixFlow.Worker.Worker(config, NullLogger<TixFlow.Worker.Worker>.Instance))
        {
            await worker.StartAsync(CancellationToken.None);
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    if (Equals(await Scalar("SELECT count(*) FROM mock_notifications WHERE order_id = @id", paid.Order), 2L)) break;
                    await Task.Delay(100);
                }
                Assert.Equal(2L, await Scalar("SELECT count(*) FROM mock_notifications WHERE order_id = @id", paid.Order));
                Assert.Equal("Expired", await Scalar("SELECT status FROM holds WHERE id = @id", unpaid.Hold));
            }
            finally { await worker.StopAsync(CancellationToken.None); }
        }
        using (var restarted = new TixFlow.Worker.Worker(config, NullLogger<TixFlow.Worker.Worker>.Instance))
        {
            await restarted.StartAsync(CancellationToken.None);
            try
            {
                await Task.Delay(700);
                Assert.Equal(2L, await Scalar("SELECT count(*) FROM tickets t JOIN order_items i ON i.id = t.order_item_id WHERE i.order_id = @id", paid.Order));
                Assert.Equal(2L, await Scalar("SELECT count(*) FROM mock_notifications WHERE order_id = @id", paid.Order));
                Assert.Equal(0, await Scalar("SELECT held_quantity FROM ticket_inventories WHERE ticket_type_id = @id", Type));
            }
            finally { await restarted.StopAsync(CancellationToken.None); }
        }
    }

    [Fact]
    public async Task CheckoutAndSettlementWorkWithProductionDatabaseRetryStrategy()
    {
        using var retryFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<TixFlowDbContext>>();
            services.AddDbContext<TixFlowDbContext>(options => options.UseNpgsql(factory.ConnectionString,
                postgres => postgres.EnableRetryOnFailure()).UseSnakeCaseNamingConvention());
        }));
        var client = retryFactory.CreateClient();
        string subject = $"phase7-retry-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(subject, subject + "@example.test", ["Customer"]));
        await using var booking = await Book(paid: true, providedClient: client);
        Assert.Equal(1L, await Scalar("SELECT count(*) FROM outbox_messages WHERE type = 'OrderPaid' AND payload->>'orderId' = @id::text", booking.Order));
    }

    [Fact]
    public async Task PaidOrderIssuesOneTicketPerUnitDespiteDuplicateAndConcurrentMessages()
    {
        await using var booking = await Book(paid: true);
        var handler = new TicketDelivery(factory.ConnectionString);
        Delivery message = Message("OrderPaid", booking.Order);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => handler.ProcessAsync(message)));
        await handler.ProcessAsync(message with { Id = Guid.NewGuid() });
        Assert.Equal(2L, await Scalar("SELECT count(*) FROM tickets t JOIN order_items i ON i.id = t.order_item_id WHERE i.order_id = @id", booking.Order));
        var tickets = await booking.Client.GetFromJsonAsync<JsonElement>("/api/v1/tickets");
        Assert.Equal(2, tickets.GetProperty("data").GetArrayLength());
        var ticket = tickets.GetProperty("data")[0];
        Assert.False(ticket.TryGetProperty("qrToken", out _));
        var qr = (await booking.Client.GetFromJsonAsync<JsonElement>($"/api/v1/tickets/{ticket.GetProperty("id").GetGuid()}/qr")).GetProperty("data");
        Assert.StartsWith("data:image/png;base64,", qr.GetProperty("qrImageDataUrl").GetString());
        Assert.StartsWith("tf1_", qr.GetProperty("qrToken").GetString());
        var qrAgain = (await booking.Client.GetFromJsonAsync<JsonElement>($"/api/v1/tickets/{ticket.GetProperty("id").GetGuid()}/qr")).GetProperty("data");
        Assert.Equal(qr.GetProperty("qrToken").GetString(), qrAgain.GetProperty("qrToken").GetString());
        using var stranger = Client("Customer");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/tickets/{ticket.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/tickets/{ticket.GetProperty("id").GetGuid()}/qr")).StatusCode);
        var otherTickets = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/tickets");
        Assert.Empty(otherTickets.GetProperty("data").EnumerateArray());

        Delivery notification = Message("TicketsIssued", booking.Order);
        await handler.ProcessAsync(notification);
        await handler.ProcessAsync(notification);
        await handler.ProcessAsync(notification with { Id = Guid.NewGuid() });
        Assert.Equal(2L, await Scalar("SELECT count(*) FROM mock_notifications WHERE order_id = @id", booking.Order));
    }

    [Fact]
    public async Task CheckInHasOneWinnerReplaysSameKeyAndRejectsOtherScanners()
    {
        await using var booking = await Book(paid: true);
        await using var window = await OpenCheckInWindow();
        await new TicketDelivery(factory.ConnectionString).ProcessAsync(Message("OrderPaid", booking.Order));
        var tickets = (await booking.Client.GetFromJsonAsync<JsonElement>("/api/v1/tickets")).GetProperty("data");
        string token = await Token(booking.Client, tickets[0].GetProperty("id").GetGuid());
        using var admin = Client("Admin");
        using var organizer = Client("Organizer");
        var request = new { eventSessionId = Session, qrToken = token, reason = "Gate verification" };
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(booking.Client, "/api/v1/tickets/check-ins", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(organizer, "/api/v1/tickets/check-ins", request)).StatusCode);
        var scans = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Post(admin, "/api/v1/tickets/check-ins", request, $"scan-{i}")));
        Assert.Single(scans, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(9, scans.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        int winner = Array.FindIndex(scans, x => x.StatusCode == HttpStatusCode.OK);
        var repeated = await Post(admin, "/api/v1/tickets/check-ins", request, $"scan-{winner}");
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(await scans[winner].Content.ReadAsStringAsync(), await repeated.Content.ReadAsStringAsync());
        Assert.Equal("true", repeated.Headers.GetValues("Idempotency-Replayed").Single());
        var reused = await Post(admin, "/api/v1/tickets/check-ins", new { eventSessionId = Session,
            qrToken = await Token(booking.Client, tickets[1].GetProperty("id").GetGuid()), reason = "Gate verification" }, $"scan-{winner}");
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal(1L, await Scalar("SELECT count(*) FROM ticket_check_ins WHERE ticket_id IN (SELECT t.id FROM tickets t JOIN order_items i ON i.id = t.order_item_id WHERE i.order_id = @id)", booking.Order));
        foreach (var scan in scans) scan.Dispose();
    }

    [Fact]
    public async Task CheckInEnforcesWindowSessionReasonAndAuditsManualEntry()
    {
        await using var booking = await Book(paid: true);
        await new TicketDelivery(factory.ConnectionString).ProcessAsync(Message("OrderPaid", booking.Order));
        var tickets = (await booking.Client.GetFromJsonAsync<JsonElement>("/api/v1/tickets")).GetProperty("data");
        Guid id = tickets[0].GetProperty("id").GetGuid();
        string token = await Token(booking.Client, id);
        using var admin = Client("Admin");
        using var organizer = Client("Organizer");
        Assert.Equal(HttpStatusCode.Forbidden, (await organizer.GetAsync($"/api/v1/tickets/{id}/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/v1/tickets/{id}/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/tickets/{id}/qr?supportReason=Support")).StatusCode);
        Assert.Equal(1L, await Scalar("SELECT count(*) FROM audit_logs WHERE entity_id = @id::text AND action = 'TicketQrViewed'", id));
        var closed = await Post(admin, "/api/v1/check-ins", new { eventSessionId = Session, qrToken = token, reason = "Gate" });
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal("CHECK_IN_WINDOW_CLOSED", (await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var window = await OpenCheckInWindow();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, "/api/v1/check-ins", new { eventSessionId = Session, qrToken = token })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(admin, "/api/v1/check-ins", new { eventSessionId = Guid.NewGuid(), qrToken = token, reason = "Gate" })).StatusCode);
        var manual = await Post(admin, "/api/v1/check-ins", new { eventSessionId = Session,
            ticketCode = tickets[0].GetProperty("ticketCode").GetString(), reason = "Damaged screen" });
        Assert.Equal(HttpStatusCode.OK, manual.StatusCode);
        Assert.Equal(1L, await Scalar("SELECT count(*) FROM audit_logs WHERE entity_id = @id::text AND action = 'TicketCheckedIn' AND data->>'method' = 'Manual'", id));
        await Execute("UPDATE event_sessions SET starts_at_utc = now() - interval '2 hours', ends_at_utc = now() WHERE id = @id", Session);
        var ended = await Post(admin, "/api/v1/check-ins", new { eventSessionId = Session,
            qrToken = await Token(booking.Client, tickets[1].GetProperty("id").GetGuid()), reason = "Gate" });
        Assert.Equal(HttpStatusCode.Conflict, ended.StatusCode);
    }

    [Fact]
    public void QrKeyRotationPreservesPreviouslyIssuedProof()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tixflow-qr-test-" + Guid.NewGuid());
        IConfiguration Config(string key) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Tickets:KeyDirectory"] = directory, ["Tickets:ActiveKeyId"] = key }).Build();
        try
        {
            Guid id = Guid.NewGuid(); string nonce = "nonce";
            string original = new QrTokens(Config("v1")).Create(id, nonce, "v1");
            var rotated = new QrTokens(Config("v2"));
            Assert.Equal(original, rotated.Create(id, nonce, "v1"));
            Assert.NotEqual(original, rotated.Create(id, nonce, "v2"));
            Assert.NotEqual(original, rotated.Create(Guid.NewGuid(), nonce, "v1"));
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task ConcurrentExpiryReleasesInventoryExactlyOnce()
    {
        await using var booking = await Book(paid: false);
        await Execute("UPDATE holds SET expires_at_utc = clock_timestamp() - interval '1 second' WHERE id = @id", booking.Hold);
        var expiry = new HoldExpiry(factory.ConnectionString);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => expiry.RunBatchAsync()));
        Assert.Equal("Expired", await Scalar("SELECT status FROM holds WHERE id = @id", booking.Hold));
        Assert.Equal("Expired", await Scalar("SELECT status FROM orders WHERE id = @id", booking.Order));
        Assert.Equal(0, await Scalar("SELECT held_quantity FROM ticket_inventories WHERE ticket_type_id = @id", Type));
        Assert.Equal(0, await expiry.RunBatchAsync());
        await Assert.ThrowsAsync<PoisonMessageException>(() => new TicketDelivery(factory.ConnectionString).ProcessAsync(Message("OrderPaid", booking.Order)));
        Assert.Equal(0L, await Scalar("SELECT count(*) FROM tickets t JOIN order_items i ON i.id = t.order_item_id WHERE i.order_id = @id", booking.Order));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task ExpiryPaymentRacePreservesStateAndInventory(int deadlineSeconds)
    {
        await using var booking = await Book(paid: false);
        await Execute("""
            INSERT INTO payments(id, order_id, idempotency_key, method, status, amount, currency)
            SELECT id, id, id::text, 'Mock', 'Pending', total_amount, currency FROM orders WHERE id = @id
            """, booking.Order);
        await Execute($"UPDATE holds SET expires_at_utc = clock_timestamp() + interval '{deadlineSeconds} seconds' WHERE id = @id", booking.Hold);
        using var service = factory.CreateClient();
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.MockServiceToken());
        var callback = new { providerEventId = Guid.NewGuid().ToString(), paymentId = booking.Order,
            providerTransactionId = Guid.NewGuid().ToString(), outcome = "Succeeded", amount = 500000m,
            currency = "VND", occurredAtUtc = DateTimeOffset.UtcNow };
        var expiry = new HoldExpiry(factory.ConnectionString);
        await Task.WhenAll(expiry.RunBatchAsync(), service.PostAsJsonAsync("/api/v1/payment-webhooks/mock", callback));
        await expiry.RunBatchAsync();
        var duplicate = await service.PostAsJsonAsync("/api/v1/payment-webhooks/mock", callback);
        duplicate.EnsureSuccessStatusCode();
        string status = (string)(await Scalar("SELECT status FROM orders WHERE id = @id", booking.Order))!;
        Assert.Contains(status, new[] { "Paid", "Expired" });
        Assert.Equal(status == "Paid" ? "Confirmed" : "Expired", await Scalar("SELECT status FROM holds WHERE id = @id", booking.Hold));
        Assert.Equal(0, await Scalar("SELECT held_quantity FROM ticket_inventories WHERE ticket_type_id = @id", Type));
        Assert.Equal(status == "Paid" ? 2 : 0, await Scalar("SELECT sold_quantity FROM ticket_inventories WHERE ticket_type_id = @id", Type));
        Assert.Equal(status == "Paid" ? 1L : 0L, await Scalar("SELECT count(*) FROM outbox_messages WHERE type = 'OrderPaid' AND payload->>'orderId' = @id::text", booking.Order));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReservedSeatsAreIssuedOrReleasedWithoutDuplicateAllocation(bool paid)
    {
        await using var booking = await Book(paid, reserved: true);
        if (paid)
        {
            var handler = new TicketDelivery(factory.ConnectionString);
            await handler.ProcessAsync(Message("OrderPaid", booking.Order));
            await handler.ProcessAsync(Message("OrderPaid", booking.Order));
            Assert.Equal(2L, await Scalar("SELECT count(DISTINCT seat_id) FROM tickets t JOIN order_items i ON i.id = t.order_item_id WHERE i.order_id = @id", booking.Order));
            Assert.Equal(2L, await Scalar("SELECT count(*) FROM seat_allocations WHERE order_item_id IN (SELECT id FROM order_items WHERE order_id = @id) AND status = 'Sold'", booking.Order));
        }
        else
        {
            await Execute("UPDATE holds SET expires_at_utc = now() - interval '1 second' WHERE id = @id", booking.Hold);
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => new HoldExpiry(factory.ConnectionString).RunBatchAsync()));
            Assert.Equal(0L, await Scalar("SELECT count(*) FROM seat_allocations WHERE order_item_id IN (SELECT id FROM order_items WHERE order_id = @id) AND status <> 'Released'", booking.Order));
        }
    }

    [Fact]
    public async Task AbandonedOutboxLeaseAndBrokerFailureRecoverWithoutLosingMessage()
    {
        // Isolate publisher candidates from phase 6 test leftovers in this disposable database.
        await Execute("UPDATE outbox_messages SET processed_at_utc = now() WHERE type <> 'MockPaymentRequested'", Guid.Empty);
        Guid id = Guid.NewGuid();
        await Execute("INSERT INTO outbox_messages(id,type,payload,occurred_at_utc) VALUES (@id,'OrderPaid','{}',now())", id);
        var store = new OutboxStore(factory.ConnectionString);
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.ClaimAsync()));
        OutboxClaim claimed = Assert.Single(claims, x => x is not null)!;
        Assert.Equal(id, claimed.Message.Id);
        Assert.Null(await store.ClaimAsync());
        var unavailableBroker = new ConfigurationBuilder().AddConfiguration(BrokerConfig)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RabbitMq:Host"] = "127.0.0.1", ["RabbitMq:Port"] = "1" }).Build();
        Assert.Throws<RabbitMQ.Client.Exceptions.BrokerUnreachableException>(() =>
        {
            using var broker = new RabbitDelivery(unavailableBroker);
            broker.Publish(claimed.Message);
        });
        Assert.Equal(DBNull.Value, await Scalar("SELECT processed_at_utc FROM outbox_messages WHERE id = @id", id));
        await store.RetryAsync(claimed);
        Assert.Null(await store.ClaimAsync());
        await Execute("UPDATE outbox_messages SET next_attempt_at_utc = now() - interval '1 second' WHERE id = @id", id);
        var retry = Assert.IsType<OutboxClaim>(await store.ClaimAsync());
        await Execute("UPDATE outbox_messages SET leased_until_utc = now() - interval '1 second' WHERE id = @id", id);
        var restartedStore = new OutboxStore(factory.ConnectionString);
        var recovered = Assert.IsType<OutboxClaim>(await restartedStore.ClaimAsync());
        Assert.NotEqual(retry.LeaseId, recovered.LeaseId);
        await store.CompleteAsync(retry); // Old process cannot complete the new lease.
        Assert.Equal(DBNull.Value, await Scalar("SELECT processed_at_utc FROM outbox_messages WHERE id = @id", id));
        await restartedStore.CompleteAsync(recovered);
        Assert.NotEqual(DBNull.Value, await Scalar("SELECT processed_at_utc FROM outbox_messages WHERE id = @id", id));
    }

    [Fact]
    public async Task RabbitDuplicateUnackedRestartAndPoisonMessageAreSafe()
    {
        await using var booking = await Book(paid: true);
        var handler = new TicketDelivery(factory.ConnectionString);
        using var broker = new RabbitDelivery(BrokerConfig);
        Delivery message = Message("OrderPaid", booking.Order);
        using var connection = TestConnection();
        using var channel = connection.CreateModel();
        channel.QueuePurge(RabbitDelivery.Queue); channel.QueuePurge(RabbitDelivery.DeadQueue);
        broker.Publish(message);
        using (var abandoned = connection.CreateModel())
        {
            Assert.NotNull(abandoned.BasicGet(RabbitDelivery.Queue, false));
            // The consumer disappears before DB commit / ack.
        }
        bool consumed = false;
        for (int i = 0; i < 30 && !consumed; i++) { consumed = await broker.ConsumeOneAsync(handler); if (!consumed) await Task.Delay(100); }
        Assert.True(consumed);
        broker.Publish(message);
        Assert.True(await broker.ConsumeOneAsync(handler));
        Assert.Equal(2L, await Scalar("SELECT count(*) FROM tickets t JOIN order_items i ON i.id = t.order_item_id WHERE i.order_id = @id", booking.Order));
        broker.Publish(new(Guid.NewGuid(), "UnknownEvent", "{}", "poison-test"));
        Assert.True(await broker.ConsumeOneAsync(handler));
        var dead = channel.BasicGet(RabbitDelivery.DeadQueue, true);
        Assert.NotNull(dead);
        Assert.Equal("poison-test", dead.BasicProperties.CorrelationId);
    }

    [Fact]
    public async Task BrokerConsumerTransientFailureUsesDelayedRetry()
    {
        using var broker = new RabbitDelivery(BrokerConfig);
        using var connection = TestConnection(); using var channel = connection.CreateModel();
        channel.QueuePurge(RabbitDelivery.Queue);
        channel.QueuePurge(RabbitDelivery.Queue + ".retry.1");
        var message = Message("OrderPaid", Guid.NewGuid());
        broker.Publish(message);
        var unavailableDb = new TicketDelivery("Host=127.0.0.1;Port=1;Database=tixflow_auth_tests;Username=test;Password=test;Timeout=1");
        Assert.True(await broker.ConsumeOneAsync(unavailableDb));
        Assert.Equal(1u, channel.MessageCount(RabbitDelivery.Queue + ".retry.1"));
        RabbitMQ.Client.BasicGetResult? retry = null;
        for (int i = 0; i < 30 && retry is null; i++) { await Task.Delay(100); retry = channel.BasicGet(RabbitDelivery.Queue, true); }
        Assert.NotNull(retry);
        Assert.Equal(message.Id.ToString(), retry.BasicProperties.MessageId);
        Assert.Equal(1, retry.BasicProperties.Headers["tixflow-attempt"]);
    }

    private static IConnection TestConnection() => new ConnectionFactory
    { HostName = "localhost", Port = 15673, UserName = "tixflow_test", Password = "isolated_test_only" }.CreateConnection();

    private static async Task<string> Token(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/tickets/{id}/qr")).GetProperty("data").GetProperty("qrToken").GetString()!;

    private async Task<CleanupScope> OpenCheckInWindow()
    {
        var start = (DateTime)(await Scalar("SELECT starts_at_utc FROM event_sessions WHERE id = @id", Session))!;
        var end = (DateTime)(await Scalar("SELECT ends_at_utc FROM event_sessions WHERE id = @id", Session))!;
        await Execute("UPDATE event_sessions SET starts_at_utc = now() + interval '30 minutes', ends_at_utc = now() + interval '2 hours' WHERE id = @id", Session);
        return new(async () =>
        {
            await using var connection = new NpgsqlConnection(factory.ConnectionString); await connection.OpenAsync();
            await using var command = new NpgsqlCommand("UPDATE event_sessions SET starts_at_utc = @start, ends_at_utc = @end WHERE id = @id", connection);
            command.Parameters.AddWithValue("start", start); command.Parameters.AddWithValue("end", end); command.Parameters.AddWithValue("id", Session);
            await command.ExecuteNonQueryAsync();
        });
    }

    private static Delivery Message(string type, Guid order) => new(Guid.NewGuid(), type, JsonSerializer.Serialize(new { orderId = order }), order.ToString());
    private HttpClient Client(string role)
    {
        string identity = $"phase7-{Guid.NewGuid():N}";
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(identity, identity + "@example.test", [role]));
        return client;
    }

    private async Task<Booking> Book(bool paid, bool reserved = false, HttpClient? providedClient = null)
    {
        var client = providedClient ?? Client("Customer");
        Guid ticketType = reserved ? Guid.Parse("50000000-0000-0000-0000-000000000001") : Type;
        Guid[] seats = reserved ? [Guid.Parse("60000000-0000-0000-0000-000000000001"), Guid.Parse("60000000-0000-0000-0000-000000000002")] : [];
        var holdResponse = await Post(client, "/api/v1/holds", new { eventSessionId = Session,
            items = new[] { new { ticketTypeId = ticketType, inventoryMode = reserved ? "ReservedSeating" : "GeneralAdmission", quantity = 2, seatIds = seats } } });
        holdResponse.EnsureSuccessStatusCode();
        Guid hold = (await holdResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
        var orderResponse = await Post(client, $"/api/v1/holds/{hold}/checkout", null);
        orderResponse.EnsureSuccessStatusCode();
        Guid order = (await orderResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
        if (paid)
        {
            var payment = await Post(client, $"/api/v1/orders/{order}/payments/mock", new { outcome = "Succeeded" });
            payment.EnsureSuccessStatusCode();
            for (int i = 0; i < 100; i++)
            {
                if ((string?)await Scalar("SELECT status FROM orders WHERE id = @id", order) == "Paid") break;
                await Task.Delay(100);
            }
            Assert.Equal("Paid", await Scalar("SELECT status FROM orders WHERE id = @id", order));
        }
        return new(client, hold, order, async () =>
        {
            await Execute("""
                DELETE FROM tickets WHERE order_item_id IN (SELECT id FROM order_items WHERE order_id = @id);
                DELETE FROM seat_allocations WHERE order_item_id IN (SELECT id FROM order_items WHERE order_id = @id);
                DELETE FROM outbox_messages WHERE payload->>'orderId' = @id::text;
                DELETE FROM orders WHERE id = @id;
                """, order);
            await Execute("DELETE FROM holds WHERE id = @id", hold);
            await Execute("UPDATE ticket_inventories SET held_quantity = 0, sold_quantity = 0 WHERE ticket_type_id = @id", ticketType);
        });
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object? body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); request.Headers.Add("X-Expected-Version", "0");
        return client.SendAsync(request);
    }
    private async Task<object?> Scalar(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync();
    }
    private async Task Execute(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }
    private sealed record Booking(HttpClient Client, Guid Hold, Guid Order, Func<Task> Cleanup) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { Client.Dispose(); await Cleanup(); }
    }
    private sealed record CleanupScope(Func<Task> Cleanup) : IAsyncDisposable
    { public async ValueTask DisposeAsync() => await Cleanup(); }
}
