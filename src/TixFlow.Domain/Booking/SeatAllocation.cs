using TixFlow.Domain.Common;
using TixFlow.Domain.Events;
using TixFlow.Domain.Orders;

namespace TixFlow.Domain.Booking;

public sealed class SeatAllocation : Entity
{
    private SeatAllocation()
    {
    }

    public SeatAllocation(Guid seatId, Guid holdItemId, DateTimeOffset expiresAtUtc)
    {
        SeatId = seatId;
        HoldItemId = holdItemId;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid SeatId { get; private set; }

    public Guid? HoldItemId { get; private set; }

    public Guid? OrderItemId { get; private set; }

    public SeatAllocationStatus Status { get; private set; } = SeatAllocationStatus.Held;

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public Seat Seat { get; private set; } = null!;

    public HoldItem? HoldItem { get; private set; }

    public OrderItem? OrderItem { get; private set; }
}
