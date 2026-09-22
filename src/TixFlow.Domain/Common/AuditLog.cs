namespace TixFlow.Domain.Common;

public sealed class AuditLog : Entity
{
    private AuditLog()
    {
    }

    public AuditLog(
        Guid? actorUserId,
        string action,
        string entityType,
        string entityId,
        string? data,
        string? ipAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        ActorUserId = actorUserId;
        Action = action.Trim();
        EntityType = entityType.Trim();
        EntityId = entityId.Trim();
        Data = data;
        IpAddress = ipAddress;
    }

    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public string? Data { get; private set; }

    public string? IpAddress { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
}
