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

    public Event Event { get; private set; } = null!;

    public ICollection<TicketType> TicketTypes { get; private set; } = new List<TicketType>();

    public ICollection<Seat> Seats { get; private set; } = new List<Seat>();
}
