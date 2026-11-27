using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using TixFlow.Api.Authentication;
using TixFlow.Domain.Common;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;
using TixFlow.Domain.WaitingRoom;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.WaitingRoom;

public static class WaitingRoomManagementModule
{
    public static IEndpointRouteBuilder MapWaitingRoomManagement(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/management/event-sessions/{sessionId:guid}/waiting-room-policy")
            .WithTags("Waiting Room Management")
            .RequireAuthorization("OrganizerOrAdmin");
        group.MapGet("", GetAsync)
            .WithSummary("Read a session's waiting room policy and queue depth")
            .Produces<WaitingRoomPolicyResponse>(200)
            .Produces(404).Produces(503);
        group.MapPut("", PutAsync)
            .WithSummary("Change a waiting room policy with version check and audit")
            .Produces<WaitingRoomPolicyResponse>(200)
            .Produces(400).Produces(404).Produces(409).Produces(503);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid sessionId, HttpContext context,
        TixFlowDbContext db, IConnectionMultiplexer redis, CancellationToken ct)
    {
        if (!await CanManageAsync(sessionId, context, db, ct)) return Problem(404, "SESSION_NOT_FOUND");
        WaitingRoomPolicy? policy = await db.WaitingRoomPolicies.AsNoTracking()
            .SingleOrDefaultAsync(item => item.EventSessionId == sessionId, ct);
        long? queueDepth = await TryQueueDepthAsync(redis, sessionId);
        return Results.Ok(ToResponse(sessionId, policy, queueDepth));
    }

    private static async Task<IResult> PutAsync(Guid sessionId, WaitingRoomPolicyChange request,
        HttpContext context, TixFlowDbContext db, IConnectionMultiplexer redis, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.State) || request.MaxGrantsPerSecond is < 1 or > 1000
            || request.MaxConcurrentAdmissions is < 1 or > 100000
            || request.ExpectedVersion < 0 || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < 10 or > 500)
            return Problem(400, "INVALID_WAITING_ROOM_POLICY");
        if (!await CanManageAsync(sessionId, context, db, ct)) return Problem(404, "SESSION_NOT_FOUND");
        if (request.State == WaitingRoomPolicyState.Active)
        {
            var sale = await db.EventSessions.AsNoTracking()
                .Where(item => item.Id == sessionId)
                .Select(item => new { item.Status, EventStatus = item.Event.Status })
                .SingleAsync(ct);
            if (sale.Status != SessionStatus.OnSale || sale.EventStatus != EventStatus.Published)
                return Problem(409, "SALES_WINDOW_CLOSED");
        }
        try
        {
            // Active must be rejected when Redis cannot enforce the shared grant budget.
            // Pausing/closing must still work while Redis is unavailable.
            if (request.State == WaitingRoomPolicyState.Active) await redis.GetDatabase().PingAsync();
            long? queueDepth = request.State == WaitingRoomPolicyState.Active
                ? await redis.GetDatabase().SortedSetLengthAsync($"wr:queue:{sessionId:D}")
                : await TryQueueDepthAsync(redis, sessionId);
            return await db.Database.CreateExecutionStrategy().ExecuteAsync<IResult>(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                WaitingRoomPolicy? policy = await db.WaitingRoomPolicies
                    .FromSqlInterpolated($"SELECT * FROM waiting_room_policies WHERE event_session_id = {sessionId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct);
                int version = policy?.Version ?? 0;
                if (version != request.ExpectedVersion) return Problem(409, "POLICY_VERSION_CONFLICT");
                WaitingRoomPolicyState oldState = policy?.State ?? WaitingRoomPolicyState.Disabled;
                int oldRate = policy?.MaxGrantsPerSecond ?? 5;
                int oldCapacity = policy?.MaxConcurrentAdmissions ?? 500;
                int newCapacity = request.MaxConcurrentAdmissions ?? oldCapacity;
                if (policy is null)
                {
                    policy = new WaitingRoomPolicy(sessionId, request.MaxGrantsPerSecond,
                        newCapacity);
                    db.WaitingRoomPolicies.Add(policy);
                }
                policy.Change(request.State, request.MaxGrantsPerSecond, newCapacity);
                User actor = (User)context.Items[LocalUserMiddleware.UserKey]!;
                db.AuditLogs.Add(new AuditLog(actor.Id, "WaitingRoomPolicyChanged", "WaitingRoomPolicy",
                    sessionId.ToString("D"), JsonSerializer.Serialize(new
                    {
                        oldState = oldState.ToString(), oldRate, oldCapacity,
                        newState = request.State.ToString(), newRate = request.MaxGrantsPerSecond,
                        newCapacity, version = policy.Version,
                        reason = request.Reason.Trim(), queueDepth
                    }), context.Connection.RemoteIpAddress?.ToString()));
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Results.Ok(ToResponse(sessionId, policy, queueDepth));
            });
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            context.Response.Headers.RetryAfter = "3";
            return Problem(503, "WAITING_ROOM_UNAVAILABLE");
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException
            { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Problem(409, "POLICY_VERSION_CONFLICT");
        }
    }

    private static async Task<bool> CanManageAsync(Guid sessionId, HttpContext context,
        TixFlowDbContext db, CancellationToken ct)
    {
        User actor = (User)context.Items[LocalUserMiddleware.UserKey]!;
        return await db.EventSessions.AsNoTracking().AnyAsync(item => item.Id == sessionId
            && (actor.Role == UserRole.Admin || item.Event.Organizer.OwnerUserId == actor.Id), ct);
    }

    private static async Task<long?> TryQueueDepthAsync(IConnectionMultiplexer redis, Guid sessionId)
    {
        try { return await redis.GetDatabase().SortedSetLengthAsync($"wr:queue:{sessionId:D}"); }
        catch (Exception exception) when (exception is RedisException or TimeoutException) { return null; }
    }

    private static WaitingRoomPolicyResponse ToResponse(Guid sessionId, WaitingRoomPolicy? policy, long? queueDepth) =>
        new(new WaitingRoomPolicyData(sessionId, policy?.State ?? WaitingRoomPolicyState.Disabled,
            policy?.MaxGrantsPerSecond ?? 5, policy?.Version ?? 0, queueDepth,
            policy?.MaxConcurrentAdmissions ?? 500));

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status,
        title: code, detail: code, extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record WaitingRoomPolicyChange(WaitingRoomPolicyState State, int MaxGrantsPerSecond,
    int ExpectedVersion, string Reason, int? MaxConcurrentAdmissions = null);
public sealed record WaitingRoomPolicyResponse(WaitingRoomPolicyData Data);
public sealed record WaitingRoomPolicyData(Guid EventSessionId, WaitingRoomPolicyState State,
    int MaxGrantsPerSecond, int Version, long? QueueDepth, int MaxConcurrentAdmissions);
