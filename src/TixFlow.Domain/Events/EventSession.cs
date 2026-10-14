using TixFlow.Domain.Common;

namespace TixFlow.Domain.Events;

public sealed class EventSession : Entity
{
    private EventSession()
    {
    }

    public EventSession(Guid eventId, string name, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAtUtc), "Session end must be after its start.");
        }

        EventId = eventId;
        Name = name.Trim();
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    public Guid EventId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset StartsAtUtc { get; private set; }

    public DateTimeOffset EndsAtUtc { get; private set; }

    public SessionStatus Status { get; private set; } = SessionStatus.Scheduled;

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public long Version { get; private set; }

    public Event Event { get; private set; } = null!;

    public ICollection<TicketType> TicketTypes { get; private set; } = new List<TicketType>();

    public ICollection<Seat> Seats { get; private set; } = new List<Seat>();

    public void Update(string name, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        if (Status is SessionStatus.Cancelled or SessionStatus.Completed)
            throw new InvalidOperationException("Cancelled or completed sessions cannot be edited.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (endsAtUtc <= startsAtUtc)
            throw new ArgumentOutOfRangeException(nameof(endsAtUtc), "Session end must be after its start.");

        Name = name.Trim();
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Touch();
    }

    public void SetStatus(SessionStatus status, DateTimeOffset decisionTimeUtc)
    {
        bool valid = (Status, status) switch
        {
            (SessionStatus.Scheduled, SessionStatus.OnSale or SessionStatus.Cancelled) => true,
            (SessionStatus.OnSale, SessionStatus.SoldOut or SessionStatus.Cancelled or SessionStatus.Completed) => true,
            (SessionStatus.SoldOut, SessionStatus.OnSale or SessionStatus.Cancelled or SessionStatus.Completed) => true,
            _ => false
        };
        if (!valid)
            throw new InvalidOperationException($"Session cannot transition from {Status} to {status}.");
        if (status == SessionStatus.Completed && EndsAtUtc > decisionTimeUtc)
            throw new InvalidOperationException("A session cannot be completed before it ends.");

        Status = status;
        Touch();
    }

    private void Touch()
    {
        Version++;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
