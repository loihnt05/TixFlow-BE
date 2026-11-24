using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TixFlow.Domain.Events;
using TixFlow.Domain.WaitingRoom;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.WaitingRoom;

// A short, bounded cache collapses simultaneous status polls for the same sale.
// A stale open result can be returned for at most 100 ms; Booking still checks sale state.
public sealed class WaitingRoomSaleStateCache : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 512 });
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<bool?> IsClosedAsync(Guid sessionId, TixFlowDbContext db, CancellationToken ct)
    {
        if (cache.TryGetValue(sessionId, out SaleState? cached)) return cached!.IsClosed;
        SemaphoreSlim gate = gates[sessionId.GetHashCode() & 63];
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(sessionId, out cached)) return cached!.IsClosed;
            bool? closed = await db.EventSessions.AsNoTracking()
                .Where(session => session.Id == sessionId)
                .Select(session => (bool?)(session.Status == SessionStatus.SoldOut
                    || session.Status == SessionStatus.Cancelled || session.Status == SessionStatus.Completed
                    || session.Event.Status != EventStatus.Published
                    || db.WaitingRoomPolicies.Any(policy => policy.EventSessionId == sessionId
                        && (policy.State == WaitingRoomPolicyState.Closed
                            || policy.State == WaitingRoomPolicyState.Disabled))))
                .SingleOrDefaultAsync(ct);
            cache.Set(sessionId, new SaleState(closed), new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(100), Size = 1
            });
            return closed;
        }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        cache.Dispose();
        foreach (SemaphoreSlim gate in gates) gate.Dispose();
    }

    private sealed record SaleState(bool? IsClosed);
}
