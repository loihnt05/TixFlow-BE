using TixFlow.Domain.Booking;
using TixFlow.Domain.Common;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;

namespace TixFlow.Domain.Orders;

public sealed class Order : Entity
{
    private Order()
    {
    }

    public Order(Guid userId, Guid eventSessionId, Guid holdId, string orderNumber, decimal totalAmount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);

        if (totalAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAmount));
        }

        UserId = userId;
        EventSessionId = eventSessionId;
        HoldId = holdId;
        OrderNumber = orderNumber.Trim().ToUpperInvariant();
        TotalAmount = totalAmount;
    }

    public Guid UserId { get; private set; }

    public Guid EventSessionId { get; private set; }

    public Guid HoldId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;

    public decimal TotalAmount { get; private set; }

    public string Currency { get; private set; } = "VND";

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public User User { get; private set; } = null!;

    public EventSession EventSession { get; private set; } = null!;

    public Hold Hold { get; private set; } = null!;

    public ICollection<OrderItem> Items { get; private set; } = new List<OrderItem>();

    public ICollection<Payment> Payments { get; private set; } = new List<Payment>();
}
