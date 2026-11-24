using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using TixFlow.Application.WaitingRoom;
using TixFlow.Domain.Events;
using TixFlow.Domain.WaitingRoom;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.WaitingRoom;

public static class WaitingRoomModule
{
    public static IEndpointRouteBuilder MapWaitingRoomModule(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/waiting-room")
            .WithTags("Waiting Room")
            .RequireAuthorization("Customer");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (Exception exception) when (exception is RedisException or TimeoutException)
            {
                context.HttpContext.Response.Headers.RetryAfter = "3";
                return Problem(503, "WAITING_ROOM_UNAVAILABLE", true);
            }
        });
        group.MapPost("/join", JoinAsync)
            .WithSummary("Join or resume the queue for an event session")
            .Produces<WaitingRoomResponse<WaitingRoomJoinData>>(200)
            .Produces<WaitingRoomProblem>(400, "application/problem+json")
            .Produces<WaitingRoomProblem>(404, "application/problem+json")
            .Produces<WaitingRoomProblem>(409, "application/problem+json")
            .Produces<WaitingRoomProblem>(503, "application/problem+json");
        group.MapGet("/status", StatusAsync)
            .WithSummary("Read the current queue state and admission token")
            .Produces<WaitingRoomResponse<WaitingRoomStatusData>>(200)
            .Produces<WaitingRoomProblem>(400, "application/problem+json")
            .Produces<WaitingRoomProblem>(404, "application/problem+json")
            .Produces<WaitingRoomProblem>(503, "application/problem+json");
        group.MapDelete("/{queueId:guid}", AbandonAsync)
            .WithSummary("Leave the queue")
            .Produces(204)
            .Produces<WaitingRoomProblem>(400, "application/problem+json")
            .Produces<WaitingRoomProblem>(404, "application/problem+json")
            .Produces<WaitingRoomProblem>(503, "application/problem+json");
        return endpoints;
    }

    private static async Task<IResult> JoinAsync(JoinRequest request, HttpContext context,
        TixFlowDbContext db, IServiceProvider services, CancellationToken ct)
    {
        if (request.EventSessionId == Guid.Empty)
            return Problem(400, "EVENT_SESSION_REQUIRED");
        var session = await db.EventSessions.AsNoTracking()
            .Where(item => item.Id == request.EventSessionId)
            .Select(item => new { item.Status, EventStatus = item.Event.Status })
            .SingleOrDefaultAsync(ct);
        if (session is null) return Problem(404, "SESSION_NOT_FOUND");
        if (session.EventStatus != EventStatus.Published
            || session.Status is SessionStatus.SoldOut or SessionStatus.Cancelled or SessionStatus.Completed)
            return Problem(409, "SALES_WINDOW_CLOSED");

        WaitingRoomPolicy? policy = await db.WaitingRoomPolicies.AsNoTracking()
            .SingleOrDefaultAsync(item => item.EventSessionId == request.EventSessionId, ct);
        if (policy is null || policy.State == WaitingRoomPolicyState.Disabled)
            return Results.Ok(new { data = new { mode = "Direct" } });
        if (policy.State == WaitingRoomPolicyState.Closed)
            return Problem(409, "WAITING_ROOM_CLOSED");

        string subject = context.User.FindFirst("sub")!.Value;
        IWaitingRoomStore store = services.GetRequiredService<IWaitingRoomStore>();
        QueueEntrySnapshot entry = await store.JoinAsync(request.EventSessionId, subject, ct);
        return Results.Ok(new { data = new { mode = "Waiting", entry.QueueId, entry.Status,
            entry.PositionEstimate, pollAfterMs = PollAfterMs(entry.QueueId) } });
    }

    private static async Task<IResult> StatusAsync(Guid queueId, HttpContext context,
        IWaitingRoomStore store, AdmissionTokens tokens, WaitingRoomSaleStateCache saleState,
        TixFlowDbContext db, CancellationToken ct)
    {
        if (queueId == Guid.Empty) return Problem(400, "QUEUE_ID_REQUIRED");
        bool measure = context.RequestServices.GetRequiredService<IConfiguration>()
            .GetValue<bool>("WaitingRoom:DiagnosticsEnabled");
        Stopwatch? timer = measure ? Stopwatch.StartNew() : null;
        QueueEntrySnapshot? entry = await store.GetAsync(queueId, context.User.FindFirst("sub")!.Value, ct);
        double redisMs = timer?.Elapsed.TotalMilliseconds ?? 0;
        if (entry is null) return Problem(404, "QUEUE_ENTRY_NOT_FOUND");
        timer?.Restart();
        if (entry.Status is "Waiting" or "Admitted")
        {
            bool? saleClosed = await saleState.IsClosedAsync(entry.EventSessionId, db, ct);
            if (saleClosed != false)
            {
                await store.RejectAsync(queueId, context.User.FindFirst("sub")!.Value, ct);
                entry = await store.GetAsync(queueId, context.User.FindFirst("sub")!.Value, ct);
            }
        }
        double saleDbMs = timer?.Elapsed.TotalMilliseconds ?? 0;
        if (entry is null) return Problem(404, "QUEUE_ENTRY_NOT_FOUND");
        string? admissionToken = entry.Status == "Admitted"
            ? tokens.Issue(entry, context.User.FindFirst("sub")!.Value) : null;
        if (measure)
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("TixFlow.WaitingRoom.Diagnostics")
                .LogInformation("WaitingRoomTiming status RedisMs {RedisMs} SaleDbMs {SaleDbMs}",
                    redisMs, saleDbMs);
        return Results.Ok(new { data = new { entry.QueueId, entry.EventSessionId, entry.Status,
            entry.PositionEstimate, entry.ExpiresAtUnixMs, entry.GrantId, pollAfterMs = PollAfterMs(entry.QueueId),
            admissionToken } });
    }

    private static async Task<IResult> AbandonAsync(Guid queueId, HttpContext context,
        IWaitingRoomStore store, CancellationToken ct)
    {
        if (queueId == Guid.Empty) return Problem(400, "QUEUE_ID_REQUIRED");
        bool found = await store.AbandonAsync(queueId, context.User.FindFirst("sub")!.Value, ct);
        return found ? Results.NoContent() : Problem(404, "QUEUE_ENTRY_NOT_FOUND");
    }

    private static IResult Problem(int status, string code, bool retryable = false) =>
        Results.Problem(statusCode: status, title: code, detail: code,
            extensions: new Dictionary<string, object?> { ["code"] = code, ["retryable"] = retryable });

    // The same client keeps a stable interval while the flash crowd's first polls
    // are distributed across two seconds instead of landing at one deadline.
    private static int PollAfterMs(Guid queueId) => 3000 + Math.Abs(queueId.GetHashCode() % 2001);

    private sealed record JoinRequest(Guid EventSessionId);
}

public sealed record WaitingRoomResponse<T>(T Data);
public sealed record WaitingRoomJoinData(string Mode, Guid? QueueId, string? Status,
    long? PositionEstimate, int? PollAfterMs);
public sealed record WaitingRoomStatusData(Guid QueueId, Guid EventSessionId, string Status,
    long? PositionEstimate, long ExpiresAtUnixMs, Guid? GrantId, int PollAfterMs,
    string? AdmissionToken);
public sealed record WaitingRoomProblem(string? Type, string? Title, int? Status,
    string? Detail, string? Code, bool? Retryable, string? TraceId);
