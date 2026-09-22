using TixFlow.Domain.Common;
using TixFlow.Domain.Events;

namespace TixFlow.Domain.Booking;

public sealed class TicketInventory : Entity
{
    private TicketInventory()
    {
    }

    public TicketInventory(Guid ticketTypeId, int totalQuantity)
    {
        if (totalQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalQuantity));
        }

        TicketTypeId = ticketTypeId;
        TotalQuantity = totalQuantity;
    }

    public Guid TicketTypeId { get; private set; }

    public int TotalQuantity { get; private set; }

    public int HeldQuantity { get; private set; }

    public int SoldQuantity { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public TicketType TicketType { get; private set; } = null!;
}
