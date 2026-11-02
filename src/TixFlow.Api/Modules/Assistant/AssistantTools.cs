using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TixFlow.Api.Modules.Booking;
using TixFlow.Domain.Events;

namespace TixFlow.Api.Modules.Assistant;

public sealed class AssistantTools(IAssistantBookingApi api, IAssistantAudit audit, AssistantTokens tokens,
    IHttpContextAccessor accessor, TimeProvider clock, IOptions<BookingOptions> booking, IOptions<AssistantOptions> options)
{
    private int calls;
    public List<HoldProposal> Proposals { get; } = [];
    public List<string> ToolsUsed { get; } = [];
    public string Subject => accessor.HttpContext!.User.FindFirst("sub")!.Value;

    public async Task<T> Audited<T>(string name, Func<Task<T>> operation, CancellationToken ct)
    {
        if (Interlocked.Increment(ref calls) > options.Value.MaxToolCalls)
            throw new AssistantProblem(429, "ASSISTANT_TOOL_LIMIT");
        await audit.WriteAsync(name, "started", ct);
        ToolsUsed.Add(name);
        try
        {
            var result = await operation();
            await audit.WriteAsync(name, "completed", ct);
            return result;
        }
        catch
        {
            // No raw arguments, prompt, tool results, exception messages or tokens enter audit.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await audit.WriteAsync(name, "failed", deadline.Token);
            throw;
        }
    }

    [Description("Search published TixFlow events. Dates are ISO dates. Use the user's explicit filters; ask for missing preferences.")]
    public Task<JsonElement> SearchEvents(SearchEventsRequest request, CancellationToken ct) => Audited("SearchEvents", async () =>
    {
        if (new[] { request.Query, request.Location, request.Category }.Any(s => s?.Length > 200)
            || request.DateFrom > request.DateTo) throw new AssistantProblem(422, "INVALID_SEARCH");
        var result = await api.SearchAsync(request, ct);
        return JsonSerializer.SerializeToElement(result.GetProperty("events").EnumerateArray().Select(e => new
        {
            id = e.GetProperty("id"), name = e.GetProperty("name"), slug = e.GetProperty("slug"),
            category = e.GetProperty("category"), venue = e.GetProperty("venue"),
            sessions = e.GetProperty("sessions").EnumerateArray().Select(s => new
            { id = s.GetProperty("id"), name = s.GetProperty("name"), startsAtUtc = s.GetProperty("startsAtUtc") })
        }));
    }, ct);

    [Description("Get a published event and its sessions and ticket types using an event ID returned by SearchEvents.")]
    public Task<JsonElement> GetEventDetails(Guid eventId, CancellationToken ct) => Audited("GetEventDetails", async () =>
    {
        var e = await api.DetailsAsync(eventId, ct);
        return JsonSerializer.SerializeToElement(new { id = e.GetProperty("id"), name = e.GetProperty("name"),
            slug = e.GetProperty("slug"), sessions = e.GetProperty("sessions").EnumerateArray().Take(10).Select(s => new
            { id = s.GetProperty("id"), name = s.GetProperty("name"), startsAtUtc = s.GetProperty("startsAtUtc"),
                status = s.GetProperty("status"), ticketTypes = s.GetProperty("ticketTypes").EnumerateArray().Take(20).Select(t => new
                { id = t.GetProperty("id"), name = t.GetProperty("name"), price = t.GetProperty("price"), currency = t.GetProperty("currency"), inventoryMode = t.GetProperty("inventoryMode") }) }) });
    }, ct);

    [Description("Get live availability for a session. This is a snapshot, not a reservation.")]
    public Task<JsonElement> GetAvailability(Guid sessionId, CancellationToken ct) => Audited("GetAvailability", async () =>
    {
        var result = await api.AvailabilityAsync(sessionId, ct);
        return JsonSerializer.SerializeToElement(new { eventSessionId = sessionId, snapshotAtUtc = result.GetProperty("snapshotAtUtc"),
            ticketTypes = result.GetProperty("ticketTypes").EnumerateArray().Take(20).Select(t => new
            { id = t.GetProperty("id"), name = t.GetProperty("name"), price = t.GetProperty("price"), currency = t.GetProperty("currency"),
                inventoryMode = t.GetProperty("inventoryMode"), availableQuantity = t.GetProperty("availableQuantity"),
                maxPerOrder = t.GetProperty("maxPerOrder"), seats = t.GetProperty("seats").EnumerateArray().Take(40),
                seatsTruncated = t.GetProperty("seats").GetArrayLength() > 40 }) });
    }, ct);

    [Description("Prepare a server-priced hold preview for the user's selected event, session, ticket type, quantity and seats. Does NOT reserve tickets. The user must press the confirmation button in the UI.")]
    public Task<HoldPreview> PrepareHold(PrepareHoldRequest request, CancellationToken ct) => Audited("PrepareHold", async () =>
    {
        if (request.EventId == Guid.Empty || request.SessionId == Guid.Empty || request.TicketTypeId == Guid.Empty
            || request.Quantity is < 1 or > 20 || request.SeatIds?.Length > 20)
            throw new AssistantProblem(422, "INVALID_SELECTION");
        var e = await api.DetailsAsync(request.EventId, ct);
        var session = e.GetProperty("sessions").EnumerateArray().FirstOrDefault(s => s.GetProperty("id").GetGuid() == request.SessionId);
        if (session.ValueKind == JsonValueKind.Undefined) throw new AssistantProblem(404, "SESSION_NOT_FOUND");
        var now = clock.GetUtcNow();
        if (session.GetProperty("status").GetString() != "OnSale" || session.GetProperty("startsAtUtc").GetDateTimeOffset() <= now
            || !InSale(e, now)) throw new AssistantProblem(409, "SALE_CLOSED");
        var availability = await api.AvailabilityAsync(request.SessionId, ct);
        var ticket = availability.GetProperty("ticketTypes").EnumerateArray().FirstOrDefault(t => t.GetProperty("id").GetGuid() == request.TicketTypeId);
        if (ticket.ValueKind == JsonValueKind.Undefined) throw new AssistantProblem(404, "TICKET_TYPE_NOT_FOUND");
        if (request.Quantity > ticket.GetProperty("maxPerOrder").GetInt32() || !InSale(ticket, now))
            throw new AssistantProblem(422, "INVALID_SELECTION");
        if (request.Quantity > ticket.GetProperty("availableQuantity").GetInt32()) throw new AssistantProblem(409, "INSUFFICIENT_INVENTORY");
        var mode = Enum.Parse<InventoryMode>(ticket.GetProperty("inventoryMode").GetString()!);
        Guid[] seatIds = request.SeatIds ?? [];
        if (seatIds.Distinct().Count() != seatIds.Length || (mode == InventoryMode.ReservedSeating ? seatIds.Length != request.Quantity : seatIds.Length != 0))
            throw new AssistantProblem(422, "INVALID_SEATS");
        var seats = new List<string>();
        foreach (var id in seatIds)
        {
            var seat = ticket.GetProperty("seats").EnumerateArray().FirstOrDefault(s => s.GetProperty("id").GetGuid() == id);
            if (seat.ValueKind == JsonValueKind.Undefined || seat.GetProperty("status").GetString() != "Available")
                throw new AssistantProblem(409, "SEAT_UNAVAILABLE");
            seats.Add($"{seat.GetProperty("section").GetString()} / {seat.GetProperty("rowLabel").GetString()} / {seat.GetProperty("seatNumber").GetString()}");
        }
        decimal price = ticket.GetProperty("price").GetDecimal();
        var preview = new HoldPreview(request.EventId, e.GetProperty("name").GetString()!, e.GetProperty("slug").GetString()!,
            request.SessionId, session.GetProperty("name").GetString()!, session.GetProperty("startsAtUtc").GetDateTimeOffset(),
            request.TicketTypeId, ticket.GetProperty("name").GetString()!, mode.ToString(), request.Quantity, seatIds, seats.ToArray(),
            price, price * request.Quantity, ticket.GetProperty("currency").GetString()!, booking.Value.HoldDurationMinutes, now.AddMinutes(2));
        var command = new BookingModule.CreateHoldRequest(request.SessionId,
            [new(request.TicketTypeId, mode, request.Quantity, seatIds)],
            new(preview.TotalAmount, preview.Currency, preview.HoldDurationMinutes));
        var token = tokens.Protect("confirmation", new ConfirmationState(Subject, Guid.NewGuid(), preview, command));
        Proposals.Add(new HoldProposal(token, preview));
        return preview; // The confirmation token is never supplied to the model.
    }, ct);

    private static bool InSale(JsonElement item, DateTimeOffset now) =>
        (!item.TryGetProperty("saleStartsAtUtc", out var start) || start.ValueKind == JsonValueKind.Null || start.GetDateTimeOffset() <= now)
        && (!item.TryGetProperty("saleEndsAtUtc", out var end) || end.ValueKind == JsonValueKind.Null || end.GetDateTimeOffset() > now);
}
