using TixFlow.Domain.Common;

namespace TixFlow.Domain.Messaging;

public sealed class OutboxMessage : Entity
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(string type, string payload, DateTimeOffset occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        Type = type.Trim();
        Payload = payload;
        OccurredAtUtc = occurredAtUtc;
    }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }
}
