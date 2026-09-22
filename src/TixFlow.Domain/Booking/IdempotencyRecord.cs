using TixFlow.Domain.Common;
using TixFlow.Domain.Identity;

namespace TixFlow.Domain.Booking;

public sealed class IdempotencyRecord : Entity
{
    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(
        Guid userId,
        string scope,
        string key,
        string requestHash,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        UserId = userId;
        Scope = scope.Trim();
        Key = key.Trim();
        RequestHash = requestHash;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid UserId { get; private set; }

    public string Scope { get; private set; } = string.Empty;

    public string Key { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public int? ResponseStatusCode { get; private set; }

    public string? ResponseBody { get; private set; }

    public bool IsCompleted { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public User User { get; private set; } = null!;
}
