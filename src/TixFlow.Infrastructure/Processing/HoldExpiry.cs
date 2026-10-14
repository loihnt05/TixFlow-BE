using Npgsql;

namespace TixFlow.Infrastructure.Processing;

public sealed class HoldExpiry(string connectionString)
{
    public async Task<int> RunBatchAsync(int batchSize = 100, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        var candidates = new List<(Guid Hold, Guid Session, Guid Event)>();
        await using (var query = new NpgsqlCommand("""
            SELECT h.id, h.event_session_id, s.event_id FROM holds h
            JOIN event_sessions s ON s.id = h.event_session_id
            WHERE h.status = 'Active' AND h.expires_at_utc <= clock_timestamp()
            ORDER BY h.expires_at_utc, h.id LIMIT @limit
            """, connection))
        {
            query.Parameters.AddWithValue("limit", Math.Clamp(batchSize, 1, 1000));
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) candidates.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2)));
        }
        int expired = 0;
        foreach (var candidate in candidates)
        {
            await using var tx = await connection.BeginTransactionAsync(ct);
            // Same aggregate lock order as checkout/payment. Skip busy aggregates;
            // never claim a Hold first and then wait for its Event.
            if (await Scalar("SELECT id FROM events WHERE id = @id FOR UPDATE SKIP LOCKED", candidate.Event) is null
                || await Scalar("SELECT id FROM event_sessions WHERE id = @id FOR UPDATE SKIP LOCKED", candidate.Session) is null
                || await Scalar("""
                    SELECT id FROM holds WHERE id = @id AND status = 'Active'
                    AND expires_at_utc <= clock_timestamp() FOR UPDATE SKIP LOCKED
                    """, candidate.Hold) is null) continue;
            await Execute("SELECT id FROM orders WHERE hold_id = @id ORDER BY id FOR UPDATE");
            await Execute("""
                UPDATE payments SET status = 'Failed', failure_reason = 'ORDER_EXPIRED', updated_at_utc = clock_timestamp()
                WHERE order_id IN (SELECT id FROM orders WHERE hold_id = @id) AND status = 'Pending'
                """);
            await Execute("""
                SELECT i.id FROM ticket_inventories i
                WHERE i.ticket_type_id IN (SELECT ticket_type_id FROM hold_items WHERE hold_id = @id)
                ORDER BY i.ticket_type_id FOR UPDATE
                """);
            // Constraints reject an inconsistent decrement instead of silently clamping it.
            await Execute("""
                UPDATE ticket_inventories i SET held_quantity = i.held_quantity - h.quantity,
                    version = i.version + 1, updated_at_utc = clock_timestamp()
                FROM (SELECT ticket_type_id, SUM(quantity)::integer quantity FROM hold_items
                      WHERE hold_id = @id GROUP BY ticket_type_id) h
                WHERE i.ticket_type_id = h.ticket_type_id
                """);
            await Execute("""
                UPDATE seat_allocations SET status = 'Released', expires_at_utc = NULL, updated_at_utc = clock_timestamp()
                WHERE hold_item_id IN (SELECT id FROM hold_items WHERE hold_id = @id) AND status = 'Held';
                UPDATE orders SET status = 'Expired', version = version + 1, updated_at_utc = clock_timestamp()
                WHERE hold_id = @id AND status = 'PendingPayment';
                UPDATE holds SET status = 'Expired', version = version + 1, updated_at_utc = clock_timestamp()
                WHERE id = @id AND status = 'Active';
                """);
            await tx.CommitAsync(ct);
            expired++;

            async Task<object?> Scalar(string sql, Guid id)
            {
                await using var cmd = new NpgsqlCommand(sql, connection, tx);
                cmd.Parameters.AddWithValue("id", id);
                return await cmd.ExecuteScalarAsync(ct);
            }
            async Task Execute(string sql)
            {
                await using var cmd = new NpgsqlCommand(sql, connection, tx);
                cmd.Parameters.AddWithValue("id", candidate.Hold);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        return expired;
    }
}
