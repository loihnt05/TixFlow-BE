using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace TixFlow.Api.Tests;

public sealed class HoldConcurrencyTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static readonly Guid SessionId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid ReservedTicketTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid FlashTicketTypeId = Guid.Parse("50000000-0000-0000-0000-000000000012");
    private static readonly Guid Seat1 = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid Seat2 = Guid.Parse("60000000-0000-0000-0000-000000000002");
    private static readonly Guid Seat3 = Guid.Parse("60000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task SameIdempotencyKeyReplaysAndCancellationReleasesExactlyOnce()
    {
        TestIdentity identity = Identity("idempotency");
        using HttpClient client = Client(identity);
        await WarmProfileAsync(client);
        await ResetBookingStateAsync(identity.Email);

        try
        {
            object body = GeneralAdmission(2);
            using HttpResponseMessage created = await CreateHoldAsync(client, "same-create-key", body);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            JsonElement first = await Body(created);
            Guid holdId = first.GetProperty("data").GetProperty("id").GetGuid();

            using HttpResponseMessage replay = await CreateHoldAsync(client, "same-create-key", body);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal(holdId, (await Body(replay)).GetProperty("data").GetProperty("id").GetGuid());

            using HttpResponseMessage reused = await CreateHoldAsync(
                client, "same-create-key", GeneralAdmission(1));
            Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
            Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await Body(reused)).GetProperty("code").GetString());

            JsonElement get = await Body(await client.GetAsync($"/api/v1/holds/{holdId}"));
            Assert.Equal(2, get.GetProperty("data").GetProperty("items")[0].GetProperty("quantity").GetInt32());

            using HttpResponseMessage cancelled = await CancelHoldAsync(client, holdId, 0, "same-cancel-key");
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.Equal("Cancelled", (await Body(cancelled)).GetProperty("data").GetProperty("status").GetString());

            using HttpResponseMessage cancelReplay = await CancelHoldAsync(client, holdId, 0, "same-cancel-key");
            Assert.Equal(HttpStatusCode.OK, cancelReplay.StatusCode);
            Assert.Equal("true", cancelReplay.Headers.GetValues("Idempotency-Replayed").Single());

            InventoryCounts counts = await InventoryAsync(FlashTicketTypeId);
            Assert.Equal(0, counts.Held);
            Assert.Equal(50, counts.Available);
        }
        finally
        {
            await ResetBookingStateAsync(identity.Email);
        }
    }

    [Fact]
    public async Task ConcurrentRequestsForOneSeatHaveExactlyOneWinner()
    {
        TestIdentity identity = Identity("one-seat");
        using HttpClient client = Client(identity);
        await WarmProfileAsync(client);
        await ResetBookingStateAsync(identity.Email);

        try
        {
            Task<HttpResponseMessage>[] attempts = Enumerable.Range(0, 20)
                .Select(index => CreateHoldAsync(
                    client,
                    $"one-seat-{index}",
                    Reserved([Seat1])))
                .ToArray();
            HttpResponseMessage[] responses = await Task.WhenAll(attempts);
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
                Assert.Equal(19, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

                (int activeAllocations, InventoryCounts counts) = await SeatInvariantAsync([Seat1]);
                Assert.Equal(1, activeAllocations);
                Assert.Equal(1, counts.Held);
                Assert.True(counts.Held + counts.Sold <= counts.Total);
            }
            finally
            {
                foreach (HttpResponseMessage response in responses) response.Dispose();
            }
        }
        finally
        {
            await ResetBookingStateAsync(identity.Email);
        }
    }

    [Fact]
    public async Task OverlappingMultiSeatRequestsAreAllOrNothing()
    {
        TestIdentity identity = Identity("multi-seat");
        using HttpClient client = Client(identity);
        await WarmProfileAsync(client);
        await ResetBookingStateAsync(identity.Email);

        try
        {
            HttpResponseMessage[] responses = await Task.WhenAll(
                CreateHoldAsync(client, "multi-a", Reserved([Seat1, Seat2])),
                CreateHoldAsync(client, "multi-b", Reserved([Seat2, Seat3])));
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

                (int activeAllocations, InventoryCounts counts) = await SeatInvariantAsync([Seat1, Seat2, Seat3]);
                Assert.Equal(2, activeAllocations);
                Assert.Equal(2, counts.Held);
                Assert.True(counts.Held + counts.Sold <= counts.Total);
            }
            finally
            {
                foreach (HttpResponseMessage response in responses) response.Dispose();
            }
        }
        finally
        {
            await ResetBookingStateAsync(identity.Email);
        }
    }

    [Fact]
    public async Task QuantityInventoryNeverExceedsCapacityUnderContention()
    {
        TestIdentity identity = Identity("quantity");
        using HttpClient client = Client(identity);
        await WarmProfileAsync(client);
        await ResetBookingStateAsync(identity.Email);

        try
        {
            Task<HttpResponseMessage>[] attempts = Enumerable.Range(0, 30)
                .Select(index => CreateHoldAsync(
                    client,
                    $"quantity-{index}",
                    GeneralAdmission(2)))
                .ToArray();
            HttpResponseMessage[] responses = await Task.WhenAll(attempts);
            try
            {
                Assert.Equal(25, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
                Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

                InventoryCounts counts = await InventoryAsync(FlashTicketTypeId);
                Assert.Equal(50, counts.Held);
                Assert.Equal(0, counts.Available);
                Assert.True(counts.Held + counts.Sold <= counts.Total);
            }
            finally
            {
                foreach (HttpResponseMessage response in responses) response.Dispose();
            }
        }
        finally
        {
            await ResetBookingStateAsync(identity.Email);
        }
    }

    [Fact]
    public async Task FailureOnLaterItemRollsBackEarlierInventoryAndSeatChanges()
    {
        TestIdentity identity = Identity("rollback");
        using HttpClient client = Client(identity);
        await WarmProfileAsync(client);
        await ResetBookingStateAsync(identity.Email);
        await SetHeldQuantityAsync(FlashTicketTypeId, 50);

        try
        {
            object request = new
            {
                eventSessionId = SessionId,
                items = new object[]
                {
                    new
                    {
                        ticketTypeId = ReservedTicketTypeId,
                        inventoryMode = "ReservedSeating",
                        quantity = 1,
                        seatIds = new[] { Seat1 }
                    },
                    new
                    {
                        ticketTypeId = FlashTicketTypeId,
                        inventoryMode = "GeneralAdmission",
                        quantity = 2
                    }
                }
            };

            using HttpResponseMessage response = await CreateHoldAsync(client, "rollback-later-item", request);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("INSUFFICIENT_CAPACITY", (await Body(response)).GetProperty("code").GetString());

            InventoryCounts reserved = await InventoryAsync(ReservedTicketTypeId);
            Assert.Equal(0, reserved.Held);
            (int activeAllocations, _) = await SeatInvariantAsync([Seat1]);
            Assert.Equal(0, activeAllocations);
            Assert.Equal(0, await HoldCountAsync(identity.Email));

            using HttpResponseMessage replay = await CreateHoldAsync(client, "rollback-later-item", request);
            Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
            Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        }
        finally
        {
            await ResetBookingStateAsync(identity.Email);
        }
    }

    private HttpClient Client(TestIdentity identity)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Token(identity.Subject, identity.Email, ["Customer"]));
        return client;
    }

    private static async Task WarmProfileAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> CreateHoldAsync(
        HttpClient client,
        string idempotencyKey,
        object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/holds")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> CancelHoldAsync(
        HttpClient client,
        Guid holdId,
        long version,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/holds/{holdId}/cancellations")
        {
            Content = JsonContent.Create(new { reason = "Integration test cleanup" })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Expected-Version", version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return await client.SendAsync(request);
    }

    private static object GeneralAdmission(int quantity) => new
    {
        eventSessionId = SessionId,
        items = new[]
        {
            new
            {
                ticketTypeId = FlashTicketTypeId,
                inventoryMode = "GeneralAdmission",
                quantity
            }
        }
    };

    private static object Reserved(Guid[] seats) => new
    {
        eventSessionId = SessionId,
        items = new[]
        {
            new
            {
                ticketTypeId = ReservedTicketTypeId,
                inventoryMode = "ReservedSeating",
                quantity = seats.Length,
                seatIds = seats
            }
        }
    };

    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private async Task<InventoryCounts> InventoryAsync(Guid ticketTypeId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT total_quantity, held_quantity, sold_quantity,
                   total_quantity - held_quantity - sold_quantity
            FROM ticket_inventories
            WHERE ticket_type_id = @ticket_type_id;
            """, connection);
        command.Parameters.AddWithValue("ticket_type_id", ticketTypeId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new InventoryCounts(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    private async Task<(int ActiveAllocations, InventoryCounts Counts)> SeatInvariantAsync(Guid[] seatIds)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM seat_allocations
            WHERE seat_id = ANY (@seat_ids) AND status IN ('Held', 'Sold');
            """, connection);
        command.Parameters.AddWithValue("seat_ids", seatIds);
        int active = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        return (active, await InventoryAsync(ReservedTicketTypeId));
    }

    private async Task<int> HoldCountAsync(string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM holds AS hold
            JOIN users AS app_user ON app_user.id = hold.user_id
            WHERE app_user.email = @email;
            """, connection);
        command.Parameters.AddWithValue("email", email);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task SetHeldQuantityAsync(Guid ticketTypeId, int heldQuantity)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE ticket_inventories
            SET held_quantity = @held_quantity, version = version + 1, updated_at_utc = now()
            WHERE ticket_type_id = @ticket_type_id;
            """, connection);
        command.Parameters.AddWithValue("held_quantity", heldQuantity);
        command.Parameters.AddWithValue("ticket_type_id", ticketTypeId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ResetBookingStateAsync(string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            DELETE FROM seat_allocations
            WHERE hold_item_id IN (
                SELECT item.id FROM hold_items AS item
                JOIN holds AS hold ON hold.id = item.hold_id
                JOIN users AS app_user ON app_user.id = hold.user_id
                WHERE app_user.email = @email);
            DELETE FROM holds
            WHERE user_id IN (SELECT id FROM users WHERE email = @email);
            DELETE FROM idempotency_records
            WHERE user_id IN (SELECT id FROM users WHERE email = @email);
            UPDATE ticket_inventories
            SET held_quantity = 0, sold_quantity = 0, version = version + 1, updated_at_utc = now()
            WHERE ticket_type_id IN (@reserved_ticket_type_id, @flash_ticket_type_id);
            """, connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("reserved_ticket_type_id", ReservedTicketTypeId);
        command.Parameters.AddWithValue("flash_ticket_type_id", FlashTicketTypeId);
        await command.ExecuteNonQueryAsync();
    }

    private static TestIdentity Identity(string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        return new TestIdentity($"phase5-{label}-{suffix}", $"phase5-{label}-{suffix}@example.test");
    }

    private sealed record TestIdentity(string Subject, string Email);

    private sealed record InventoryCounts(int Total, int Held, int Sold, int Available);
}
