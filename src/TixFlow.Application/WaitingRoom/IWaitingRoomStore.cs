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

    // The budget controller in the next phase will call this primitive. Redis pops one
    // live entry atomically, so concurrent controller instances cannot grant it twice.
    Task<QueueEntrySnapshot?> GrantNextAsync(Guid eventSessionId, int maxGrantsPerSecond, CancellationToken cancellationToken);
    Task<string> BindIntentAsync(Guid queueId, Guid eventSessionId, Guid grantId, string subject,
        string idempotencyKeyHash, string requestHash, CancellationToken cancellationToken);
}
