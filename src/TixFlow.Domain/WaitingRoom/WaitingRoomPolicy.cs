namespace TixFlow.Domain.WaitingRoom;

public enum WaitingRoomPolicyState
{
    Disabled,
    Active,
    Paused,
    Closed
}

public sealed class WaitingRoomPolicy
{
    private WaitingRoomPolicy() { }

    public WaitingRoomPolicy(Guid eventSessionId, int maxGrantsPerSecond, int maxConcurrentAdmissions = 500)
    {
        if (eventSessionId == Guid.Empty) throw new ArgumentException("A session is required.", nameof(eventSessionId));
        if (maxGrantsPerSecond is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxGrantsPerSecond));
        if (maxConcurrentAdmissions is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentAdmissions));
        EventSessionId = eventSessionId;
        MaxGrantsPerSecond = maxGrantsPerSecond;
        MaxConcurrentAdmissions = maxConcurrentAdmissions;
    }

    public Guid EventSessionId { get; private set; }
    public WaitingRoomPolicyState State { get; private set; } = WaitingRoomPolicyState.Disabled;
    public int MaxGrantsPerSecond { get; private set; }
    public int MaxConcurrentAdmissions { get; private set; } = 500;
    public int Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public void Change(WaitingRoomPolicyState state, int maxGrantsPerSecond, int maxConcurrentAdmissions = 500)
    {
        if (maxGrantsPerSecond is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxGrantsPerSecond));
        if (maxConcurrentAdmissions is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentAdmissions));
        State = state;
        MaxGrantsPerSecond = maxGrantsPerSecond;
        MaxConcurrentAdmissions = maxConcurrentAdmissions;
        Version++;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
