using TixFlow.Infrastructure.Processing;

namespace TixFlow.Worker;

public sealed class Worker(IConfiguration configuration, ILogger<Worker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string connection = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection is required.");
        return Task.WhenAll(Expire(connection, stoppingToken), Publish(connection, stoppingToken), Consume(connection, stoppingToken));
    }

    private async Task Expire(string connection, CancellationToken ct)
    {
        var expiry = new HoldExpiry(connection);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int count = await expiry.RunBatchAsync(ct: ct);
                if (count > 0) logger.LogInformation("Expired {HoldCount} holds.", count);
                await Task.Delay(TimeSpan.FromSeconds(count >= 100 ? 0.1 : 2), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Expiry retry: {FailureType}", ex.GetType().Name); await Task.Delay(2000, ct); }
        }
    }

    private async Task Publish(string connection, CancellationToken ct)
    {
        var store = new OutboxStore(connection);
        while (!ct.IsCancellationRequested)
        {
            OutboxClaim? claim = null;
            try
            {
                claim = await store.ClaimAsync(ct);
                if (claim is null) { await Task.Delay(500, ct); continue; }
                using var broker = new RabbitDelivery(configuration);
                broker.Publish(claim.Message);
                await store.CompleteAsync(claim, ct);
                logger.LogInformation("Published {MessageId}, correlation {CorrelationId}.", claim.Message.Id, claim.Message.CorrelationId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Outbox retry: {FailureType}", ex.GetType().Name);
                if (claim is not null)
                    try { await store.RetryAsync(claim, ct); }
                    catch (Exception retryEx) when (retryEx is not OperationCanceledException)
                    { logger.LogWarning("Outbox lease will recover: {FailureType}", retryEx.GetType().Name); }
                await Task.Delay(2000, ct);
            }
        }
    }

    private async Task Consume(string connection, CancellationToken ct)
    {
        var handler = new TicketDelivery(connection, new QrTokens(configuration));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var broker = new RabbitDelivery(configuration);
                while (!ct.IsCancellationRequested)
                    if (!await broker.ConsumeOneAsync(handler, ct)) await Task.Delay(300, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Consumer reconnect: {FailureType}", ex.GetType().Name); await Task.Delay(2000, ct); }
        }
    }
}

