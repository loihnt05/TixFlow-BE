namespace TixFlow.Application.WaitingRoom;

public sealed record QueueEntrySnapshot(
    Guid QueueId,
    Guid EventSessionId,
    string Status,
    long Sequence,
    long JoinedAtUnixMs,
    long ExpiresAtUnixMs,
    long? PositionEstimate,
    Guid? GrantId);

public interface IWaitingRoomStore
{
    Task<QueueEntrySnapshot> JoinAsync(Guid eventSessionId, string subject, CancellationToken cancellationToken);
    Task<QueueEntrySnapshot?> GetAsync(Guid queueId, string subject, CancellationToken cancellationToken);
    Task<bool> AbandonAsync(Guid queueId, string subject, CancellationToken cancellationToken);
    Task RejectAsync(Guid queueId, string subject, CancellationToken cancellationToken);

    // Redis atomically enforces both the per-second budget and concurrent admission leases.
    Task<QueueEntrySnapshot?> GrantNextAsync(Guid eventSessionId, int maxGrantsPerSecond,
        CancellationToken cancellationToken, int maxConcurrentAdmissions = 500);
    Task<string> BindIntentAsync(Guid queueId, Guid eventSessionId, Guid grantId, string subject,
        string idempotencyKeyHash, string requestHash, CancellationToken cancellationToken);
}
