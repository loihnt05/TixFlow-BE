using TixFlow.Domain.Common;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;

namespace TixFlow.Domain.Orders;

public sealed class Ticket : Entity
{
    private Ticket()
    {
    }

    public Ticket(
        Guid orderItemId,
        Guid userId,
        Guid eventSessionId,
        Guid? seatId,
        string ticketCode,
        string qrTokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(qrTokenHash);

        OrderItemId = orderItemId;
        UserId = userId;
        EventSessionId = eventSessionId;
        SeatId = seatId;
        TicketCode = ticketCode.Trim().ToUpperInvariant();
        QrTokenHash = qrTokenHash;
    }

    public Guid OrderItemId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid EventSessionId { get; private set; }

    public Guid? SeatId { get; private set; }

    public string TicketCode { get; private set; } = string.Empty;

    public string QrTokenHash { get; private set; } = string.Empty;

    public TicketStatus Status { get; private set; } = TicketStatus.Issued;

    public DateTimeOffset IssuedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CheckedInAtUtc { get; private set; }

    public OrderItem OrderItem { get; private set; } = null!;

    public User User { get; private set; } = null!;

    public EventSession EventSession { get; private set; } = null!;

    public Seat? Seat { get; private set; }
}
