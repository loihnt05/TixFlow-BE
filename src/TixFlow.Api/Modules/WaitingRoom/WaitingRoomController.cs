using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Prometheus;
using TixFlow.Application.WaitingRoom;
using TixFlow.Domain.Events;
using TixFlow.Domain.WaitingRoom;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.WaitingRoom;

// Every API replica may run this loop. Redis enforces the shared per-second budget.
public sealed class WaitingRoomController(IServiceScopeFactory scopes, ILogger<WaitingRoomController> logger)
    : BackgroundService
{
    private static readonly Gauge QueueDepth = Metrics.CreateGauge("tixflow_waiting_room_queue_depth",
        "Total queued entries across configured waiting rooms on this API replica.");
    private static readonly Gauge ActivePolicies = Metrics.CreateGauge("tixflow_waiting_room_active_policies",
        "Number of active, on-sale waiting room policies on this API replica.");
    private static readonly Gauge LastSuccess = Metrics.CreateGauge("tixflow_waiting_room_controller_last_success_seconds",
        "Unix time of the last successful waiting room controller loop.");
    private static readonly Counter Failures = Metrics.CreateCounter("tixflow_waiting_room_controller_failures_total",
        "Waiting room controller loop failures.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int sample = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TixFlowDbContext>();
                var policies = await db.WaitingRoomPolicies.AsNoTracking()
                    .Where(policy => policy.State != WaitingRoomPolicyState.Disabled)
                    .Select(policy => new { policy.EventSessionId, policy.MaxGrantsPerSecond,
                        policy.MaxConcurrentAdmissions, policy.State,
                        SaleOpen = db.EventSessions.Any(session => session.Id == policy.EventSessionId
                            && session.Status == SessionStatus.OnSale && session.Event.Status == EventStatus.Published) })
                    .ToListAsync(stoppingToken);
                var active = policies.Where(policy => policy.State == WaitingRoomPolicyState.Active
                    && policy.SaleOpen).ToArray();
                ActivePolicies.Set(active.Length);
                if (active.Length > 0)
                {
                    var store = scope.ServiceProvider.GetRequiredService<IWaitingRoomStore>();
                    foreach (var policy in active)
                        for (int index = 0; index < 200; index++)
                            if (await store.GrantNextAsync(policy.EventSessionId, policy.MaxGrantsPerSecond,
                                    stoppingToken, policy.MaxConcurrentAdmissions) is null)
                                break;
                }
                if (++sample >= 20)
                {
                    sample = 0;
                    var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
                    long depth = 0;
                    foreach (var policy in policies)
                        depth += await redis.GetDatabase().SortedSetLengthAsync($"wr:queue:{policy.EventSessionId:D}");
                    QueueDepth.Set(depth);
                }
                LastSuccess.Set(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                Failures.Inc();
                logger.LogWarning("Waiting room controller paused after {FailureType}", exception.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
