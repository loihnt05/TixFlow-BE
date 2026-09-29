using TixFlow.Domain.Common;
using TixFlow.Domain.Events;

namespace TixFlow.Domain.Booking;

public sealed class HoldItem : Entity
{
    private HoldItem()
    {
    }

    public HoldItem(Guid holdId, Guid ticketTypeId, int quantity, decimal unitPrice)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice));
        }

        HoldId = holdId;
        TicketTypeId = ticketTypeId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid HoldId { get; private set; }

    public Guid TicketTypeId { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public Hold Hold { get; private set; } = null!;

    public TicketType TicketType { get; private set; } = null!;

    public ICollection<SeatAllocation> SeatAllocations { get; private set; } = new List<SeatAllocation>();
}
