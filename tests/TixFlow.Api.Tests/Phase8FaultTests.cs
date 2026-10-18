using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using RabbitMQ.Client;
using TixFlow.Infrastructure.Processing;

namespace TixFlow.Api.Tests;

[Collection("Integration")]
[Trait("Category", "Fault")]
public sealed class Phase8FaultTests(AuthApiFactory factory)
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task InvalidQuantityCannotChangeInventory(int quantity)
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        using var client = data.Client();
        try
        {
            using var rejected = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(quantity), "invalid-quantity");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            Assert.Equal(0, await data.Count($"SELECT held_quantity FROM ticket_inventories WHERE ticket_type_id='{BaselineDataset.Type}'"));
            Assert.Equal(0, await data.Count($"SELECT count(*) FROM holds WHERE event_session_id='{BaselineDataset.Session}'"));
        }
        finally { await data.ClearAsync(); }
    }

    [Fact]
    public async Task ConcurrentDuplicateCallbacksSettleOnlyOnceAndRejectChangedPayload()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        using var client = data.Client();
        try
        {
            using var holdResponse = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "callback-hold");
            Guid hold = (await BaselineDataset.Data(holdResponse)).GetProperty("id").GetGuid();
            using var checkout = await BaselineDataset.Post(client, $"/api/v1/holds/{hold}/checkout", null, "callback-checkout");
            Guid order = (await BaselineDataset.Data(checkout)).GetProperty("id").GetGuid();
            Guid payment = Guid.NewGuid();
            // Seed a pending provider attempt without scheduling the mock simulator, so the tested
            // race consists solely of the 20 real callback requests below.
            await factory.ExecuteSqlAsync($"""
                INSERT INTO payments(id,order_id,idempotency_key,method,status,amount,currency)
                VALUES ('{payment}','{order}','phase8-{payment}','Mock','Pending',250000,'VND')
                """);
            using var service = factory.CreateClient();
            service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.MockServiceToken());
            DateTimeOffset at = DateTimeOffset.UtcNow;
            object Callback(decimal amount) => new { providerEventId = $"phase8-{payment}", paymentId = payment,
                providerTransactionId = $"phase8-txn-{payment}", outcome = "Succeeded", amount, currency = "VND", occurredAtUtc = at };
            var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.PostAsJsonAsync("/api/v1/payment-webhooks/mock", Callback(250000))));
            try
            {
                var receipts = await Task.WhenAll(responses.Select(BaselineDataset.Data));
                Assert.Equal(19, receipts.Count(x => x.GetProperty("disposition").GetString() == "Duplicate"));
                Assert.Single(receipts, x => x.GetProperty("disposition").GetString() == "Processed");
            }
            finally { foreach (var response in responses) response.Dispose(); }
            using var conflict = await service.PostAsJsonAsync("/api/v1/payment-webhooks/mock", Callback(1));
            Assert.Equal("WEBHOOK_EVENT_CONFLICT", (await BaselineDataset.Data(conflict)).GetProperty("reason").GetString());
            Assert.Equal(0, await data.Count($"SELECT held_quantity FROM ticket_inventories WHERE ticket_type_id='{BaselineDataset.Type}'"));
            Assert.Equal(1, await data.Count($"SELECT sold_quantity FROM ticket_inventories WHERE ticket_type_id='{BaselineDataset.Type}'"));
            Assert.Equal(1, await data.Count($"SELECT count(*) FROM outbox_messages WHERE type='OrderPaid' AND payload->>'orderId'='{order}'"));
        }
        finally { await data.ClearAsync(); }
    }

    [Fact]
    public async Task LostResponseAfterCommitReplaysExactlyOneHold()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        using var client = data.Client(handler: new LoseFirstResponse(factory.Server.CreateHandler()));
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "lost-response"));
            using var replay = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "lost-response");
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal(1, await data.Count($"SELECT count(*) FROM holds WHERE event_session_id='{BaselineDataset.Session}'"));
            Assert.Equal(1, await data.Count($"SELECT held_quantity FROM ticket_inventories WHERE ticket_type_id='{BaselineDataset.Type}'"));
        }
        finally { await data.ClearAsync(); }
    }

    [Fact]
    public async Task RequestCancelledWhileWaitingForDatabaseLockRollsBackAndCanRetry()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        using var client = data.Client();
        await using var blocker = new NpgsqlConnection(factory.ConnectionString);
        await blocker.OpenAsync();
        await using var tx = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand($"SELECT id FROM events WHERE id='{BaselineDataset.Event}' FOR UPDATE", blocker, tx))
            await command.ExecuteNonQueryAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<HttpResponseMessage> pending = BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "cancelled-request", deadline.Token);
        try
        {
            bool waiting = false;
            for (int i = 0; i < 100; i++)
            {
                waiting = await data.Count("SELECT count(*) FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE '%FROM events%FOR SHARE%'") > 0;
                if (waiting) break;
                await Task.Delay(25);
            }
            Assert.True(waiting, "Request must reach the lock before cancellation is injected.");
            deadline.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var response = await pending; });
            await tx.RollbackAsync();
            using var retry = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "cancelled-request");
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.False(retry.Headers.Contains("Idempotency-Replayed"));
            Assert.Equal(1, await data.Count($"SELECT count(*) FROM holds WHERE event_session_id='{BaselineDataset.Session}'"));
            Assert.Equal(1, await data.Count($"SELECT held_quantity FROM ticket_inventories WHERE ticket_type_id='{BaselineDataset.Type}'"));
        }
        finally
        {
            deadline.Cancel();
            try { using var response = await pending; } catch (OperationCanceledException) { }
            await tx.DisposeAsync();
            await data.ClearAsync();
        }
    }

    [Fact]
    public async Task BrokerHandshakeTimeoutDoesNotLoseAnOutboxLease()
    {
        Guid id = Guid.NewGuid();
        // Actual TCP peer accepts but never sends AMQP handshake bytes.
        using var blackhole = new TcpListener(IPAddress.Loopback, 0);
        blackhole.Start();
        var config = new ConfigurationBuilder().AddConfiguration(factory.BrokerConfig)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Host"] = "127.0.0.1", ["RabbitMq:Port"] = ((IPEndPoint)blackhole.LocalEndpoint).Port.ToString()
            }).Build();
        var store = new OutboxStore(factory.ConnectionString);
        using var topology = new RabbitDelivery(factory.BrokerConfig);
        await factory.ExecuteSqlAsync($"INSERT INTO outbox_messages(id,type,payload,occurred_at_utc) VALUES ('{id}','TicketsIssued','{{}}','2000-01-01')");
        try
        {
            var claim = await store.ClaimAsync();
            Assert.NotNull(claim);
            Assert.Equal(id, claim.Message.Id);
            var connect = Task.Run(() => { using var broker = new RabbitDelivery(config); });
            using var peer = await blackhole.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<RabbitMQ.Client.Exceptions.BrokerUnreachableException>(() => connect.WaitAsync(TimeSpan.FromSeconds(30)));
            await store.RetryAsync(claim);
            await factory.ExecuteSqlAsync($"UPDATE outbox_messages SET next_attempt_at_utc = now() WHERE id='{id}'");
            var retry = await store.ClaimAsync();
            Assert.NotNull(retry);
            Assert.Equal(id, retry.Message.Id);
            Assert.Equal(2, retry.Attempts);
            using var broker = new RabbitDelivery(factory.BrokerConfig);
            broker.Publish(retry.Message);
            await store.CompleteAsync(retry);
            var data = new BaselineDataset(factory);
            Assert.Equal(1, await data.Count($"SELECT count(*) FROM outbox_messages WHERE id='{id}' AND processed_at_utc IS NOT NULL"));
        }
        finally
        {
            await factory.ExecuteSqlAsync($"DELETE FROM outbox_messages WHERE id='{id}'");
            using var conn = BrokerConnection(); using var channel = conn.CreateModel(); channel.QueuePurge(RabbitDelivery.Queue);
        }
    }

    [Fact]
    public async Task KilledConsumerAfterCommitRedeliversWithoutDuplicatingTickets()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        using var client = data.Client();
        try
        {
            using var holdResponse = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(2), "crash-hold");
            Guid hold = (await BaselineDataset.Data(holdResponse)).GetProperty("id").GetGuid();
            using var checkout = await BaselineDataset.Post(client, $"/api/v1/holds/{hold}/checkout", null, "crash-checkout");
            Guid order = (await BaselineDataset.Data(checkout)).GetProperty("id").GetGuid();
            using var payment = await BaselineDataset.Post(client, $"/api/v1/orders/{order}/payments/mock", new { outcome = "Succeeded" }, "crash-payment");
            payment.EnsureSuccessStatusCode();
            for (int i = 0; i < 100 && await data.Count($"SELECT count(*) FROM orders WHERE id='{order}' AND status='Paid'") == 0; i++) await Task.Delay(100);
            Assert.Equal(1, await data.Count($"SELECT count(*) FROM orders WHERE id='{order}' AND status='Paid'"));
            using var publisher = new RabbitDelivery(factory.BrokerConfig);
            using var conn = BrokerConnection(); using var channel = conn.CreateModel();
            channel.QueuePurge(RabbitDelivery.Queue);
            var message = new Delivery(Guid.NewGuid(), "OrderPaid", JsonSerializer.Serialize(new { orderId = order }), "phase8-crash");
            publisher.Publish(message);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "FaultProbe", "TixFlow.FaultProbe.dll"));
            start.Environment["PROBE_POSTGRES"] = factory.ConnectionString;
            start.Environment["PROBE_BROKER_HOST"] = factory.BrokerHost;
            start.Environment["PROBE_BROKER_PORT"] = factory.BrokerPort.ToString();
            start.Environment["PROBE_KEY_DIRECTORY"] = Path.Combine(AppContext.BaseDirectory, "ticket-keys");
            using var process = Process.Start(start)!;
            try
            {
                Assert.Equal("COMMITTED_WITHOUT_ACK", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            BasicGetResult? redelivery = null;
            for (int i = 0; i < 100 && redelivery is null; i++)
            {
                redelivery = channel.BasicGet(RabbitDelivery.Queue, false);
                if (redelivery is null) await Task.Delay(50);
            }
            Assert.NotNull(redelivery);
            Assert.True(redelivery.Redelivered);
            channel.BasicNack(redelivery.DeliveryTag, false, true);
            bool consumed = false;
            for (int i = 0; i < 100 && !consumed; i++)
            {
                consumed = await publisher.ConsumeOneAsync(new TicketDelivery(factory.ConnectionString));
                if (!consumed) await Task.Delay(50);
            }
            Assert.True(consumed);
            Assert.Equal(2, await data.Count($"SELECT count(*) FROM tickets t JOIN order_items i ON i.id=t.order_item_id WHERE i.order_id='{order}'"));
            Assert.Equal(1, await data.Count($"SELECT count(*) FROM message_inbox WHERE message_id='{message.Id}'"));
            using var ticketList = await client.GetAsync($"/api/v1/tickets?orderId={order}");
            var tickets = await BaselineDataset.Data(ticketList);
            Assert.Equal(2, tickets.GetArrayLength());
            foreach (var ticket in tickets.EnumerateArray())
            {
                using var qr = await client.GetAsync($"/api/v1/tickets/{ticket.GetProperty("id").GetGuid()}/qr");
                Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
            }
            await factory.ExecuteSqlAsync($"DELETE FROM message_inbox WHERE message_id='{message.Id}'");
        }
        finally { await data.ClearAsync(); }
    }

    [Fact]
    public async Task CheckoutUsesServerPriceSnapshotAfterCatalogChanges()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        using var client = data.Client();
        try
        {
            using var holdResponse = await BaselineDataset.Post(client, "/api/v1/holds", new
            {
                eventSessionId = BaselineDataset.Session, totalAmount = 1,
                items = new[] { new { ticketTypeId = BaselineDataset.Type, inventoryMode = "GeneralAdmission", quantity = 2, unitPrice = 1 } }
            }, "price-hold");
            JsonElement hold = await BaselineDataset.Data(holdResponse);
            Assert.Equal(500000, hold.GetProperty("totalAmount").GetDecimal());
            await factory.ExecuteSqlAsync($"UPDATE ticket_types SET price=999999, max_per_order=1 WHERE id='{BaselineDataset.Type}'");
            using var checkout = await BaselineDataset.Post(client, $"/api/v1/holds/{hold.GetProperty("id").GetGuid()}/checkout", null, "price-checkout");
            var order = await BaselineDataset.Data(checkout);
            Assert.Equal(500000, order.GetProperty("totalAmount").GetDecimal());
            Assert.Equal(250000, order.GetProperty("items")[0].GetProperty("unitPrice").GetDecimal());
            Assert.Equal(2, order.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        }
        finally { await data.ClearAsync(); }
    }

    private IConnection BrokerConnection() => new ConnectionFactory
    { HostName = factory.BrokerHost, Port = factory.BrokerPort, UserName = "tixflow_test", Password = "isolated_test_only" }.CreateConnection();

    private sealed class LoseFirstResponse(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private bool first = true;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (!first) return response;
            first = false;
            response.Dispose();
            throw new HttpRequestException("Injected lost response after server committed.", new IOException("Connection reset"));
        }
    }
}
