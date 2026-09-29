using TixFlow.Domain.Common;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;

namespace TixFlow.Domain.Booking;

public sealed class Hold : Entity
{
    private Hold()
    {
    }

    public Hold(Guid userId, Guid eventSessionId, string idempotencyKey, DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (expiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        UserId = userId;
        EventSessionId = eventSessionId;
        IdempotencyKey = idempotencyKey.Trim();
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid UserId { get; private set; }

    public Guid EventSessionId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public HoldStatus Status { get; private set; } = HoldStatus.Active;

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public User User { get; private set; } = null!;

    public EventSession EventSession { get; private set; } = null!;

    public ICollection<HoldItem> Items { get; private set; } = new List<HoldItem>();
}
