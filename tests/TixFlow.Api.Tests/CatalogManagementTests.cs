using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace TixFlow.Api.Tests;

public sealed class CatalogManagementTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task PublicCatalogIsPagedFilteredStableAndHidesNonPublishedEvents()
    {
        using HttpClient client = factory.CreateClient();

        JsonElement first = await Body(await client.GetAsync("/api/v1/events?page=1&limit=3"));
        JsonElement repeated = await Body(await client.GetAsync("/api/v1/events?page=1&limit=3"));
        JsonElement second = await Body(await client.GetAsync("/api/v1/events?page=2&limit=3"));

        Assert.Equal(10, first.GetProperty("total").GetInt32());
        Assert.Equal(4, first.GetProperty("totalPages").GetInt32());
        string[] firstIds = first.GetProperty("events").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        string[] repeatedIds = repeated.GetProperty("events").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        string[] secondIds = second.GetProperty("events").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(firstIds, repeatedIds);
        Assert.Empty(firstIds.Intersect(secondIds));

        JsonElement technology = await Body(await client.GetAsync(
            "/api/v1/events?q=Data&category=Technology&location=H%C3%A0%20N%E1%BB%99i&dateFrom=2026-11-01&dateTo=2026-11-30"));
        Assert.Equal(1, technology.GetProperty("total").GetInt32());
        Assert.Equal("ai-data-summit-vietnam",
            technology.GetProperty("events")[0].GetProperty("slug").GetString());

        JsonElement hiddenStatus = await Body(await client.GetAsync("/api/v1/events?status=Draft"));
        Assert.Equal(0, hiddenStatus.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/events/circular-design-workshop")).StatusCode);

        JsonElement detail = await Body(await client.GetAsync("/api/v1/events/tixflow-flash-sale-demo"));
        Assert.Equal("Published", detail.GetProperty("status").GetString());
        Assert.NotEmpty(detail.GetProperty("sessions")[0].GetProperty("ticketTypes").EnumerateArray());
    }

    [Fact]
    public async Task OrganizerOwnsCatalogAndEventTransitionsAreGuarded()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using HttpClient organizer = Client("Organizer", $"organizer-{suffix}@example.test");

        using HttpResponseMessage profileResponse = await organizer.PostAsJsonAsync(
            "/api/v1/management/organizers/me",
            new { name = $"Organizer {suffix}", slug = $"organizer-{suffix}", description = "Test", contactEmail = $"contact-{suffix}@example.test" });
        Assert.Equal(HttpStatusCode.Created, profileResponse.StatusCode);

        using HttpResponseMessage venueResponse = await organizer.PostAsJsonAsync(
            "/api/v1/management/venues",
            new { name = "Test Venue", addressLine = "1 Test Street", city = "Ho Chi Minh City", countryCode = "VN", timeZone = "Asia/Ho_Chi_Minh" });
        Assert.Equal(HttpStatusCode.Created, venueResponse.StatusCode);
        JsonElement venue = await Body(venueResponse);

        DateTimeOffset startsAt = DateTimeOffset.UtcNow.AddDays(7);
        DateTimeOffset endsAt = startsAt.AddHours(3);
        using HttpResponseMessage eventResponse = await organizer.PostAsJsonAsync(
            "/api/v1/management/events",
            new
            {
                name = $"Catalog Test {suffix}", category = "Technology", description = "Ownership test",
                venueId = venue.GetProperty("id").GetGuid(), saleStartsAtUtc = DateTimeOffset.UtcNow,
                saleEndsAtUtc = startsAt
            });
        Assert.True(eventResponse.StatusCode == HttpStatusCode.Created,
            $"Expected 201, got {(int)eventResponse.StatusCode}: {await eventResponse.Content.ReadAsStringAsync()}");
        JsonElement createdEvent = await Body(eventResponse);
        Guid eventId = createdEvent.GetProperty("id").GetGuid();

        using HttpResponseMessage sessionResponse = await organizer.PostAsJsonAsync(
            $"/api/v1/management/events/{eventId}/sessions",
            new { name = "Main session", startsAtUtc = startsAt, endsAtUtc = endsAt });
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        JsonElement session = await Body(sessionResponse);

        using HttpResponseMessage ticketTypeResponse = await organizer.PostAsJsonAsync(
            $"/api/v1/management/event-sessions/{session.GetProperty("id").GetGuid()}/ticket-types",
            new
            {
                name = "General Admission", code = "GENERAL", description = "Test inventory",
                price = 250000, inventoryMode = "GeneralAdmission", capacity = 100, maxPerOrder = 4
            });
        Assert.Equal(HttpStatusCode.Created, ticketTypeResponse.StatusCode);

        using HttpResponseMessage publishResponse = await organizer.PostAsJsonAsync(
            $"/api/v1/management/events/{eventId}/publish",
            new { version = createdEvent.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        JsonElement published = await Body(publishResponse);
        Assert.Equal("Published", published.GetProperty("status").GetString());

        using HttpResponseMessage editPublished = await organizer.PatchAsJsonAsync(
            $"/api/v1/management/events/{eventId}",
            new
            {
                name = "Changed", category = "Technology", description = "Cannot edit",
                venueId = venue.GetProperty("id").GetGuid(), version = published.GetProperty("version").GetInt64()
            });
        Assert.Equal(HttpStatusCode.Conflict, editPublished.StatusCode);

        using HttpClient outsider = Client("Organizer", $"outsider-{suffix}@example.test");
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/v1/management/events/{eventId}")).StatusCode);

        using HttpClient admin = Client("Admin", $"admin-{suffix}@example.test");
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/v1/management/events/{eventId}")).StatusCode);

        using HttpClient customer = Client("Customer", $"customer-{suffix}@example.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await customer.GetAsync("/api/v1/management/events")).StatusCode);
        await CleanupAsync(suffix);
    }

    [Fact]
    public async Task SessionAndSaleWindowsRejectInvalidTimeRanges()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using HttpClient organizer = Client("Organizer", $"time-{suffix}@example.test");
        await organizer.PostAsJsonAsync("/api/v1/management/organizers/me",
            new { name = $"Time {suffix}", contactEmail = $"time-contact-{suffix}@example.test" });
        using HttpResponseMessage invalidEvent = await organizer.PostAsJsonAsync("/api/v1/management/events",
            new { name = "Invalid sale", category = "Music", saleStartsAtUtc = DateTimeOffset.UtcNow.AddDays(2), saleEndsAtUtc = DateTimeOffset.UtcNow.AddDays(1) });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEvent.StatusCode);
        using HttpResponseMessage invalidImage = await organizer.PostAsJsonAsync("/api/v1/management/events",
            new { name = "Invalid image", category = "Music", imageUrl = "javascript:alert(1)" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidImage.StatusCode);
        await CleanupAsync(suffix);
    }

    private HttpClient Client(string role, string email)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Token(Guid.NewGuid().ToString(), email, [role]));
        return client;
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
            DELETE FROM events WHERE organizer_id IN (SELECT id FROM organizers WHERE contact_email LIKE @pattern);
            DELETE FROM venues WHERE organizer_id IN (SELECT id FROM organizers WHERE contact_email LIKE @pattern);
            DELETE FROM organizers WHERE contact_email LIKE @pattern;
            DELETE FROM users WHERE email LIKE @pattern;
            """, connection);
        command.Parameters.AddWithValue("pattern", $"%{suffix}%");
        await command.ExecuteNonQueryAsync();
    }
}
