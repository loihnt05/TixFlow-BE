using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace TixFlow.Infrastructure.Processing;

public static class ProcessingMetrics
{
    public static async Task SampleAsync(string connectionString, IConfiguration configuration, CancellationToken ct)
    {
        Dictionary<string, uint>? queues = null;
        string broker = "available";
        try { using var rabbit = new RabbitDelivery(configuration); queues = rabbit.QueueDepths(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { broker = "unavailable"; }
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO processing_metrics(worker_id, sampled_at_utc, values_json)
            SELECT @worker, clock_timestamp(), jsonb_build_object(
                'broker', @broker, 'readyMessages', @queues::jsonb,
                'pendingOutbox', (SELECT count(*) FROM outbox_messages WHERE processed_at_utc IS NULL AND type <> 'MockPaymentRequested'),
                'oldestPendingSeconds', (SELECT coalesce(max(extract(epoch FROM clock_timestamp()-occurred_at_utc)),0) FROM outbox_messages WHERE processed_at_utc IS NULL AND type <> 'MockPaymentRequested'),
                'consumerLagSeconds', (SELECT coalesce(max(extract(epoch FROM i.processed_at_utc-o.occurred_at_utc)),0) FROM message_inbox i JOIN outbox_messages o ON o.id=i.message_id WHERE i.processed_at_utc > clock_timestamp()-interval '5 minutes'),
                'pendingQrImages', (SELECT count(*) FROM tickets WHERE qr_image IS NULL AND qr_nonce IS NOT NULL))
            ON CONFLICT(worker_id) DO UPDATE SET sampled_at_utc=excluded.sampled_at_utc, values_json=excluded.values_json
            """, connection);
        command.Parameters.AddWithValue("worker", Environment.MachineName);
        command.Parameters.AddWithValue("broker", broker);
        command.Parameters.AddWithValue("queues", JsonSerializer.Serialize(queues));
        await command.ExecuteNonQueryAsync(ct);
    }
}
