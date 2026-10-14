using TixFlow.Domain.Common;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;

namespace TixFlow.Domain.Booking;

public sealed class Hold : Entity
{
    private Hold()
    {
    }

    public Hold(
        Guid userId,
        Guid eventSessionId,
        string idempotencyKey,
        decimal totalAmount,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (totalAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAmount));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        UserId = userId;
        EventSessionId = eventSessionId;
        IdempotencyKey = idempotencyKey.Trim();
        TotalAmount = totalAmount;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid UserId { get; private set; }

    public Guid EventSessionId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public HoldStatus Status { get; private set; } = HoldStatus.Active;

    public string Currency { get; private set; } = "VND";

    public decimal TotalAmount { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public long Version { get; private set; }

    public User User { get; private set; } = null!;

    public EventSession EventSession { get; private set; } = null!;

    public ICollection<HoldItem> Items { get; private set; } = new List<HoldItem>();

    public void Cancel(DateTimeOffset decisionTimeUtc)
    {
        if (Status == HoldStatus.Cancelled)
        {
            return;
        }

        if (Status != HoldStatus.Active)
        {
            throw new InvalidOperationException($"Hold cannot be cancelled from {Status}.");
        }

        Status = HoldStatus.Cancelled;
        UpdatedAtUtc = decisionTimeUtc;
        Version++;
    }

    public void Expire(DateTimeOffset decisionTimeUtc)
    {
        if (Status != HoldStatus.Active)
        {
            throw new InvalidOperationException($"Hold cannot expire from {Status}.");
        }

        if (decisionTimeUtc < ExpiresAtUtc)
        {
            throw new InvalidOperationException("Hold cannot expire before its deadline.");
        }

        Status = HoldStatus.Expired;
        UpdatedAtUtc = decisionTimeUtc;
        Version++;
    }
}
