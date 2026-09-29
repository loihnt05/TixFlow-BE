namespace TixFlow.Worker;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TixFlow Worker started at {StartedAt}", DateTimeOffset.UtcNow);

        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            logger.LogDebug("Worker heartbeat at {HeartbeatAt}", DateTimeOffset.UtcNow);
        }
    }
}

