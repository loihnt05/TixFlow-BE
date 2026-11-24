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

    public WaitingRoomPolicy(Guid eventSessionId, int maxGrantsPerSecond)
    {
        if (eventSessionId == Guid.Empty) throw new ArgumentException("A session is required.", nameof(eventSessionId));
        if (maxGrantsPerSecond is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxGrantsPerSecond));
        EventSessionId = eventSessionId;
        MaxGrantsPerSecond = maxGrantsPerSecond;
    }

    public Guid EventSessionId { get; private set; }
    public WaitingRoomPolicyState State { get; private set; } = WaitingRoomPolicyState.Disabled;
    public int MaxGrantsPerSecond { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public void Change(WaitingRoomPolicyState state, int maxGrantsPerSecond)
    {
        if (maxGrantsPerSecond is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxGrantsPerSecond));
        State = state;
        MaxGrantsPerSecond = maxGrantsPerSecond;
        Version++;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
