using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace TixFlow.Infrastructure.Processing;

// Use one instance/channel per loop; IModel cannot publish concurrently.
public sealed class RabbitDelivery : IDisposable
{
    public const string Queue = "tixflow.post-payment.v1";
    public const string DeadQueue = Queue + ".dead";
    private static readonly int[] RetryDelays = [1000, 5000, 30000];
    private readonly IConnection connection;
    private readonly IModel channel;
    private volatile bool returned;

    public RabbitDelivery(IConfiguration configuration)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMq:Host"] ?? "localhost",
            Port = configuration.GetValue("RabbitMq:Port", 5672),
            UserName = configuration["RabbitMq:Username"] ?? throw new InvalidOperationException("RabbitMQ username is required."),
            Password = configuration["RabbitMq:Password"] ?? throw new InvalidOperationException("RabbitMQ password is required."),
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            ContinuationTimeout = TimeSpan.FromSeconds(5)
        };
        connection = factory.CreateConnection("tixflow-phase7");
        try
        {
            channel = connection.CreateModel();
            channel.ConfirmSelect();
            channel.BasicReturn += (_, _) => returned = true;
            channel.QueueDeclare(Queue, true, false, false);
            channel.QueueDeclare(DeadQueue, true, false, false);
            for (int i = 0; i < RetryDelays.Length; i++)
                channel.QueueDeclare($"{Queue}.retry.{i + 1}", true, false, false, new Dictionary<string, object>
                {
                    ["x-message-ttl"] = RetryDelays[i], ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = Queue
                });
        }
        catch { connection.Dispose(); throw; }
    }

    public void Publish(Delivery message)
    {
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.MessageId = message.Id.ToString();
        properties.CorrelationId = message.CorrelationId;
        properties.Type = message.Type;
        ConfirmedPublish(Queue, properties, JsonSerializer.SerializeToUtf8Bytes(message));
    }

    public async Task<bool> ConsumeOneAsync(TicketDelivery handler, CancellationToken ct = default)
    {
        var delivery = channel.BasicGet(Queue, autoAck: false);
        if (delivery is null) return false;
        try
        {
            if (delivery.Body.Length > 65536) throw new PoisonMessageException("Message is too large.");
            Delivery message;
            try { message = JsonSerializer.Deserialize<Delivery>(delivery.Body.Span) ?? throw new JsonException(); }
            catch (JsonException) { throw new PoisonMessageException("Malformed envelope."); }
            if (message.Id == Guid.Empty || message.Payload is null || message.CorrelationId is null
                || delivery.BasicProperties.MessageId != message.Id.ToString())
                throw new PoisonMessageException("Invalid envelope.");
            await handler.ProcessAsync(message, ct);
            channel.BasicAck(delivery.DeliveryTag, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            int attempt = 0;
            if (delivery.BasicProperties.Headers?.TryGetValue("tixflow-attempt", out var value) == true)
                attempt = value is int count && count >= 0 ? count : RetryDelays.Length;
            bool dead = ex is PoisonMessageException || attempt >= RetryDelays.Length;
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = delivery.BasicProperties.MessageId;
            properties.CorrelationId = delivery.BasicProperties.CorrelationId;
            properties.Type = delivery.BasicProperties.Type;
            properties.Headers = new Dictionary<string, object>
            {
                ["tixflow-attempt"] = attempt + 1,
                ["tixflow-failure"] = ex is PoisonMessageException ? "INVALID_MESSAGE" : "PROCESSING_FAILED"
            };
            // Confirm the replacement before acknowledging the original. Inbox handles crash duplicates.
            ConfirmedPublish(dead ? DeadQueue : $"{Queue}.retry.{attempt + 1}", properties, delivery.Body);
            channel.BasicAck(delivery.DeliveryTag, false);
        }
        return true;
    }

    private void ConfirmedPublish(string queue, IBasicProperties properties, ReadOnlyMemory<byte> body)
    {
        returned = false;
        channel.BasicPublish("", queue, mandatory: true, properties, body);
        channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
        if (returned) throw new IOException("Broker did not route the message.");
    }

    public void Dispose() { channel.Dispose(); connection.Dispose(); }
}
