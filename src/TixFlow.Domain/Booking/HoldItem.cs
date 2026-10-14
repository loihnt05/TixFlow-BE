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
        int maxPerOrderSnapshot,
        string ticketTypeNameSnapshot,
        decimal serviceFee = 0)
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

        ArgumentException.ThrowIfNullOrWhiteSpace(ticketTypeNameSnapshot);
        if (serviceFee < 0) throw new ArgumentOutOfRangeException(nameof(serviceFee));

        HoldId = holdId;
        TicketTypeId = ticketTypeId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        MaxPerOrderSnapshot = maxPerOrderSnapshot;
        TicketTypeNameSnapshot = ticketTypeNameSnapshot.Trim();
        ServiceFee = serviceFee;
    }

    public Guid HoldId { get; private set; }

    public Guid TicketTypeId { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int MaxPerOrderSnapshot { get; private set; }

    public string TicketTypeNameSnapshot { get; private set; } = string.Empty;

    public decimal ServiceFee { get; private set; }

    public Hold Hold { get; private set; } = null!;

    public TicketType TicketType { get; private set; } = null!;

    public ICollection<SeatAllocation> SeatAllocations { get; private set; } = new List<SeatAllocation>();
}
