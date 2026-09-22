using TixFlow.Domain.Common;

namespace TixFlow.Domain.Orders;

public sealed class PaymentWebhookEvent : Entity
{
    private PaymentWebhookEvent()
    {
    }

    public PaymentWebhookEvent(string provider, string providerEventId, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        Provider = provider.Trim();
        ProviderEventId = providerEventId.Trim();
        Payload = payload;
    }

    public string Provider { get; private set; } = string.Empty;

    public string ProviderEventId { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public string? ProcessingError { get; private set; }
}
