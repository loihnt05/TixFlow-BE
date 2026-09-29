using TixFlow.Domain.Booking;
using TixFlow.Domain.Common;
using TixFlow.Domain.Events;

namespace TixFlow.Domain.Orders;

public sealed class OrderItem : Entity
{
    private OrderItem()
    {
    }

    public OrderItem(Guid orderId, Guid ticketTypeId, int quantity, decimal unitPrice)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice));
        }

        OrderId = orderId;
        TicketTypeId = ticketTypeId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid OrderId { get; private set; }

    public Guid TicketTypeId { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public Order Order { get; private set; } = null!;

    public TicketType TicketType { get; private set; } = null!;

    public ICollection<SeatAllocation> SeatAllocations { get; private set; } = new List<SeatAllocation>();
}
