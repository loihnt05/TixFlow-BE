using System.Text.Json;
using TixFlow.Api.Modules.Booking;

namespace TixFlow.Api.Modules.Assistant;

public sealed class AssistantOptions
{
    public string Provider { get; set; } = "Disabled";
    public string Model { get; set; } = "gpt-5.4-mini";
    public string ApiKey { get; set; } = "";
    public string BookingApiBaseUrl { get; set; } = "http://localhost:8080";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxToolCalls { get; set; } = 8;
    public int MaxOutputTokens { get; set; } = 1200;
    public int RequestsPerMinute { get; set; } = 10;
    public string KeyDirectory { get; set; } = "assistant-keys";
}

public sealed record AssistantMessage(string Message, string? ConversationToken = null);
public sealed record ConversationTurn(string User, string Assistant);
public sealed record ConversationState(string Subject, DateTimeOffset ExpiresAtUtc, ConversationTurn[] Turns);
public sealed record SearchEventsRequest(string? Query = null, string? Location = null,
    string? Category = null, DateOnly? DateFrom = null, DateOnly? DateTo = null);
public sealed record PrepareHoldRequest(Guid EventId, Guid SessionId, Guid TicketTypeId, int Quantity, Guid[]? SeatIds = null);
public sealed record HoldPreview(Guid EventId, string EventName, string EventSlug, Guid SessionId,
    string SessionName, DateTimeOffset StartsAtUtc, Guid TicketTypeId, string TicketName,
    string InventoryMode, int Quantity, Guid[] SeatIds, string[] Seats, decimal UnitPrice,
    decimal TotalAmount, string Currency, int HoldDurationMinutes, DateTimeOffset ConfirmBeforeUtc);
public sealed record HoldProposal(string ConfirmationToken, HoldPreview Preview);
public sealed record ConfirmationState(string Subject, Guid Nonce, HoldPreview Preview, BookingModule.CreateHoldRequest Request);
public sealed record ConfirmHoldRequest(string ConfirmationToken, bool Confirmed);
public sealed record AssistantReply(string Message, string ConversationToken, bool Fallback,
    HoldProposal[] Proposals, string[] ToolsUsed);

public interface IAssistantProvider
{
    Task<string> ReplyAsync(IReadOnlyList<ConversationTurn> history, string message, AssistantTools tools, CancellationToken ct);
}

public interface IAssistantBookingApi
{
    Task<JsonElement> SearchAsync(SearchEventsRequest request, CancellationToken ct);
    Task<JsonElement> DetailsAsync(Guid eventId, CancellationToken ct);
    Task<JsonElement> AvailabilityAsync(Guid sessionId, CancellationToken ct);
    Task<IResult> CreateHoldAsync(BookingModule.CreateHoldRequest request, string idempotencyKey, CancellationToken ct);
}

public sealed class AssistantProblem(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
