using TixFlow.Domain.Common;
using TixFlow.Domain.Events;

namespace TixFlow.Domain.Booking;

public sealed class HoldItem : Entity
{
    private HoldItem()
    {
    }

    public HoldItem(
        Guid holdId,
        Guid ticketTypeId,
        int quantity,
        decimal unitPrice,
        int maxPerOrderSnapshot)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice));
        }

        if (maxPerOrderSnapshot <= 0 || quantity > maxPerOrderSnapshot)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPerOrderSnapshot));
        }

        HoldId = holdId;
        TicketTypeId = ticketTypeId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        MaxPerOrderSnapshot = maxPerOrderSnapshot;
    }

    public Guid HoldId { get; private set; }

    public Guid TicketTypeId { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int MaxPerOrderSnapshot { get; private set; }

    public Hold Hold { get; private set; } = null!;

    public TicketType TicketType { get; private set; } = null!;

    public ICollection<SeatAllocation> SeatAllocations { get; private set; } = new List<SeatAllocation>();
}
