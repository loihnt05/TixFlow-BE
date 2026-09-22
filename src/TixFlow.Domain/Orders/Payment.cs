using TixFlow.Domain.Common;

namespace TixFlow.Domain.Orders;

public sealed class Payment : Entity
{
    private Payment()
    {
    }

    public Payment(
        Guid orderId,
        string idempotencyKey,
        PaymentMethod method,
        decimal amount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        OrderId = orderId;
        IdempotencyKey = idempotencyKey.Trim();
        Method = method;
        Amount = amount;
    }

    public Guid OrderId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public PaymentMethod Method { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "VND";

    public string? ProviderTransactionId { get; private set; }

    public DateTimeOffset? PaidAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public Order Order { get; private set; } = null!;
}
