using TixFlow.Domain.Common;
using TixFlow.Domain.Booking;

namespace TixFlow.Domain.Events;

public sealed class Seat : Entity
{
    private Seat()
    {
    }

    public Seat(
        Guid eventSessionId,
        Guid ticketTypeId,
        string section,
        string rowLabel,
        string seatNumber,
        bool isAccessible = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(seatNumber);

        EventSessionId = eventSessionId;
        TicketTypeId = ticketTypeId;
        Section = Normalize(section);
        RowLabel = Normalize(rowLabel);
        SeatNumber = Normalize(seatNumber);
        IsAccessible = isAccessible;
    }

    public Guid EventSessionId { get; private set; }

    public Guid TicketTypeId { get; private set; }

    public string Section { get; private set; } = string.Empty;

    public string RowLabel { get; private set; } = string.Empty;

    public string SeatNumber { get; private set; } = string.Empty;

    public bool IsAccessible { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public EventSession EventSession { get; private set; } = null!;

    public TicketType TicketType { get; private set; } = null!;

    public ICollection<SeatAllocation> Allocations { get; private set; } = new List<SeatAllocation>();

    public void Update(string section, string rowLabel, string seatNumber, bool isAccessible, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(seatNumber);
        Section = Normalize(section);
        RowLabel = Normalize(rowLabel);
        SeatNumber = Normalize(seatNumber);
        IsAccessible = isAccessible;
        IsActive = isActive;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
