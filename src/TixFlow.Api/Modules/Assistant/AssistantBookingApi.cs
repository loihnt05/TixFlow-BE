using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using TixFlow.Api.Modules.Booking;

namespace TixFlow.Api.Modules.Assistant;

// Fixed, typed routes only. The model cannot choose a URL, verb, headers or credentials.
public sealed class AssistantBookingApi(HttpClient client, IHttpContextAccessor accessor) : IAssistantBookingApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<JsonElement> SearchAsync(SearchEventsRequest request, CancellationToken ct) => GetAsync(
        QueryHelpers.AddQueryString("/api/v1/events", new Dictionary<string, string?>
        {
            ["q"] = request.Query, ["location"] = request.Location, ["category"] = request.Category,
            ["dateFrom"] = request.DateFrom?.ToString("yyyy-MM-dd"),
            ["dateTo"] = request.DateTo?.ToString("yyyy-MM-dd"), ["limit"] = "5"
        }), ct);
    public Task<JsonElement> DetailsAsync(Guid eventId, CancellationToken ct) => GetAsync($"/api/v1/events/{eventId:D}", ct);
    public Task<JsonElement> AvailabilityAsync(Guid sessionId, CancellationToken ct) => GetAsync($"/api/v1/event-sessions/{sessionId:D}/availability", ct);

    private async Task<JsonElement> GetAsync(string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode) throw new AssistantProblem((int)response.StatusCode, "CATALOG_UNAVAILABLE");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public async Task<IResult> CreateHoldAsync(BookingModule.CreateHoldRequest request, string idempotencyKey, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/holds");
        // Copy only the current caller's bearer token, never expose it to tools/model output.
        message.Headers.Authorization = AuthenticationHeaderValue.Parse(accessor.HttpContext!.Request.Headers.Authorization.ToString());
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        message.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await client.SendAsync(message, ct);
        foreach (string header in new[] { "Idempotency-Replayed", "Retry-After", "Location" })
            if (response.Headers.TryGetValues(header, out var values))
                accessor.HttpContext.Response.Headers[header] = values.ToArray();
        return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
    }
}
