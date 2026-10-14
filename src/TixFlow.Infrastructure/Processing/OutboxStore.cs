using Npgsql;

namespace TixFlow.Infrastructure.Processing;

public sealed record Delivery(Guid Id, string Type, string Payload, string CorrelationId);
public sealed record OutboxClaim(Delivery Message, Guid LeaseId, int Attempts);

public sealed class OutboxStore(string connectionString)
{
    public async Task<OutboxClaim?> ClaimAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        Guid lease = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            WITH candidate AS (
                SELECT id FROM outbox_messages
                WHERE processed_at_utc IS NULL AND type <> 'MockPaymentRequested'
                  AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= clock_timestamp())
                  AND (leased_until_utc IS NULL OR leased_until_utc <= clock_timestamp())
                ORDER BY occurred_at_utc, id LIMIT 1 FOR UPDATE SKIP LOCKED
            )
            UPDATE outbox_messages o SET lease_id = @lease,
                leased_until_utc = clock_timestamp() + interval '60 seconds', attempt_count = attempt_count + 1
            FROM candidate c WHERE o.id = c.id
            RETURNING o.id, o.type, o.payload::text, COALESCE(o.correlation_id, o.id::text), o.attempt_count
            """, connection);
        command.Parameters.AddWithValue("lease", lease);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)), lease, reader.GetInt32(4));
    }

    public Task CompleteAsync(OutboxClaim claim, CancellationToken ct = default) => Update(claim, true, ct);
    public Task RetryAsync(OutboxClaim claim, CancellationToken ct = default) => Update(claim, false, ct);

    private async Task Update(OutboxClaim claim, bool success, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(success ? """
            UPDATE outbox_messages SET processed_at_utc = clock_timestamp(), lease_id = NULL,
                leased_until_utc = NULL, last_error = NULL
            WHERE id = @id AND lease_id = @lease
            """ : """
            UPDATE outbox_messages SET lease_id = NULL, leased_until_utc = NULL,
                next_attempt_at_utc = clock_timestamp() + make_interval(secs => @delay), last_error = 'BROKER_UNAVAILABLE'
            WHERE id = @id AND lease_id = @lease
            """, connection);
        command.Parameters.AddWithValue("id", claim.Message.Id);
        command.Parameters.AddWithValue("lease", claim.LeaseId);
        if (!success) command.Parameters.AddWithValue("delay", Math.Min(60, 1 << Math.Min(claim.Attempts, 6)));
        await command.ExecuteNonQueryAsync(ct);
    }
}
