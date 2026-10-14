using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace TixFlow.Api.Tests;

public sealed class InventoryManagementTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task PublicAvailabilityReturnsQuantityAndReservedSeatSnapshots()
    {
        using HttpClient client = factory.CreateClient();

        JsonElement availability = await Body(await client.GetAsync(
            "/api/v1/event-sessions/40000000-0000-0000-0000-000000000001/availability"));

        Assert.Equal("40000000-0000-0000-0000-000000000001",
            availability.GetProperty("eventSessionId").GetString());
        JsonElement[] ticketTypes = availability.GetProperty("ticketTypes").EnumerateArray().ToArray();
        JsonElement reserved = ticketTypes.Single(item =>
            item.GetProperty("inventoryMode").GetString() == "ReservedSeating");
        Assert.Equal(10, reserved.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(10, reserved.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(10, reserved.GetProperty("seats").GetArrayLength());
        Assert.All(reserved.GetProperty("seats").EnumerateArray(), seat =>
            Assert.Equal("Available", seat.GetProperty("status").GetString()));

        JsonElement flash = ticketTypes.Single(item => item.GetProperty("code").GetString() == "FLASH50");
        Assert.Equal(50, flash.GetProperty("availableQuantity").GetInt32());
    }

    [Fact]
    public async Task OrganizerManagesQuantityAndSeatInventoryWithoutBreakingInvariants()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using HttpClient organizer = Client("Organizer", $"inventory-{suffix}@example.test");
        try
        {
            await Body(await organizer.PostAsJsonAsync("/api/v1/management/organizers/me",
                new { name = $"Inventory {suffix}", contactEmail = $"inventory-contact-{suffix}@example.test" }));
            JsonElement eventItem = await Body(await organizer.PostAsJsonAsync("/api/v1/management/events",
                new { name = $"Inventory Event {suffix}", category = "Music" }));
            DateTimeOffset startsAt = DateTimeOffset.UtcNow.AddDays(10);
            JsonElement session = await Body(await organizer.PostAsJsonAsync(
                $"/api/v1/management/events/{eventItem.GetProperty("id").GetGuid()}/sessions",
                new { name = "Main", startsAtUtc = startsAt, endsAtUtc = startsAt.AddHours(3) }));
            Guid sessionId = session.GetProperty("id").GetGuid();

            JsonElement general = await Body(await organizer.PostAsJsonAsync(
                $"/api/v1/management/event-sessions/{sessionId}/ticket-types",
                new
                {
                    name = "General", code = "GA", description = "Quantity inventory", price = 100000,
                    inventoryMode = "GeneralAdmission", capacity = 20, maxPerOrder = 4
                }));
            Guid generalId = general.GetProperty("id").GetGuid();

            await SetCommittedInventoryAsync(generalId, held: 2, sold: 3);
            using HttpResponseMessage belowCommitted = await organizer.PutAsJsonAsync(
                $"/api/v1/management/ticket-types/{generalId}/capacity",
                new { capacity = 4, version = general.GetProperty("version").GetInt64() });
            Assert.Equal(HttpStatusCode.Conflict, belowCommitted.StatusCode);

            JsonElement reserved = await Body(await organizer.PostAsJsonAsync(
                $"/api/v1/management/event-sessions/{sessionId}/ticket-types",
                new
                {
                    name = "VIP", code = "VIP", description = "Reserved seats", price = 500000,
                    inventoryMode = "ReservedSeating", capacity = 2, maxPerOrder = 2,
                    seats = new[]
                    {
                        new { section = "vip", rowLabel = "a", seatNumber = "01", isAccessible = false, isActive = true },
                        new { section = "VIP", rowLabel = "A", seatNumber = "02", isAccessible = true, isActive = true }
                    }
                }));
            Assert.Equal(2, reserved.GetProperty("inventory").GetProperty("availableQuantity").GetInt32());
            Assert.Equal("VIP", reserved.GetProperty("seats")[0].GetProperty("section").GetString());

            using HttpResponseMessage duplicateSeat = await organizer.PutAsJsonAsync(
                $"/api/v1/management/ticket-types/{reserved.GetProperty("id").GetGuid()}/seat-map",
                new
                {
                    version = reserved.GetProperty("version").GetInt64(),
                    seats = new[]
                    {
                        new { section = "VIP", rowLabel = "A", seatNumber = "01", isActive = true },
                        new { section = "vip", rowLabel = "a", seatNumber = "01", isActive = true }
                    }
                });
            Assert.Equal(HttpStatusCode.Conflict, duplicateSeat.StatusCode);

            using HttpClient outsider = Client("Organizer", $"inventory-outsider-{suffix}@example.test");
            Assert.Equal(HttpStatusCode.NotFound,
                (await outsider.GetAsync($"/api/v1/management/ticket-types/{generalId}")).StatusCode);
        }
        finally
        {
            await CleanupAsync(suffix);
        }
    }

    private HttpClient Client(string role, string email)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Token(Guid.NewGuid().ToString(), email, [role]));
        return client;
    }

    private async Task SetCommittedInventoryAsync(Guid ticketTypeId, int held, int sold)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE ticket_inventories
            SET held_quantity = @held, sold_quantity = @sold, version = version + 1
            WHERE ticket_type_id = @ticket_type_id;
            """, connection);
        command.Parameters.AddWithValue("held", held);
        command.Parameters.AddWithValue("sold", sold);
        command.Parameters.AddWithValue("ticket_type_id", ticketTypeId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Expected a successful response, got {(int)response.StatusCode}: {content}");
        using JsonDocument document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private async Task CleanupAsync(string suffix)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            DELETE FROM seats WHERE event_session_id IN (
                SELECT session.id FROM event_sessions AS session
                JOIN events AS event ON event.id = session.event_id
                JOIN organizers AS organizer ON organizer.id = event.organizer_id
                WHERE organizer.contact_email LIKE @pattern);
            DELETE FROM events WHERE organizer_id IN (SELECT id FROM organizers WHERE contact_email LIKE @pattern);
            DELETE FROM organizers WHERE contact_email LIKE @pattern;
            DELETE FROM users WHERE email LIKE @pattern;
            """, connection);
        command.Parameters.AddWithValue("pattern", $"%{suffix}%");
        await command.ExecuteNonQueryAsync();
    }
}
