using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace TixFlow.Infrastructure.Processing;

public sealed class PoisonMessageException(string message) : Exception(message);

public sealed class TicketDelivery(string connectionString, QrTokens? qrTokens = null)
{
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task ProcessAsync(Delivery message, CancellationToken ct = default)
    {
        if (message.Type is not ("OrderPaid" or "TicketsIssued")) throw new PoisonMessageException("Unknown message type.");
        Guid orderId;
        try
        {
            using var payload = JsonDocument.Parse(message.Payload);
            orderId = payload.RootElement.GetProperty("orderId").GetGuid();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        { throw new PoisonMessageException("Invalid order message."); }
        if (orderId == Guid.Empty || message.CorrelationId.Length is < 1 or > 100)
            throw new PoisonMessageException("Invalid message identity.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var inbox = new NpgsqlCommand("INSERT INTO message_inbox(message_id) VALUES (@id) ON CONFLICT DO NOTHING", connection, tx))
        {
            inbox.Parameters.AddWithValue("id", message.Id);
            if (await inbox.ExecuteNonQueryAsync(ct) == 0) { await tx.CommitAsync(ct); return; }
        }
        // Follow Event -> Session -> Order before inserts acquire foreign-key locks.
        // This also serializes issuance with check-in and parent transitions.
        Guid eventId, eventSessionId;
        await using (var reference = new NpgsqlCommand("""
            SELECT s.event_id, s.id FROM orders o JOIN event_sessions s ON s.id = o.event_session_id WHERE o.id = @id
            """, connection, tx))
        {
            reference.Parameters.AddWithValue("id", orderId);
            await using var reader = await reference.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new PoisonMessageException("Order is missing.");
            eventId = reader.GetGuid(0); eventSessionId = reader.GetGuid(1);
        }
        foreach (var target in new[] { (Sql: "SELECT id FROM events WHERE id = @id FOR UPDATE", Id: eventId),
            (Sql: "SELECT id FROM event_sessions WHERE id = @id FOR UPDATE", Id: eventSessionId) })
        {
            await using var command = new NpgsqlCommand(target.Sql, connection, tx);
            command.Parameters.AddWithValue("id", target.Id);
            await command.ExecuteNonQueryAsync(ct);
        }
        // Serialize different delivery IDs referring to the same order as well.
        Guid userId, sessionId;
        await using (var order = new NpgsqlCommand("SELECT user_id, event_session_id, status FROM orders WHERE id = @id FOR UPDATE", connection, tx))
        {
            order.Parameters.AddWithValue("id", orderId);
            await using var reader = await order.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new PoisonMessageException("Order is missing.");
            if (reader.GetString(2) != "Paid") throw new PoisonMessageException("Order is not paid.");
            userId = reader.GetGuid(0); sessionId = reader.GetGuid(1);
        }
        if (message.Type == "OrderPaid")
        {
            var items = new List<(Guid Id, int Quantity, string Mode)>();
            await using (var query = new NpgsqlCommand("""
                SELECT i.id, i.quantity, t.inventory_mode FROM order_items i
                JOIN ticket_types t ON t.id = i.ticket_type_id WHERE i.order_id = @id ORDER BY i.id
                """, connection, tx))
            {
                query.Parameters.AddWithValue("id", orderId);
                await using var reader = await query.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) items.Add((reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2)));
            }
            if (items.Count == 0) throw new PoisonMessageException("Order has no items.");
            foreach (var item in items)
            {
                var seats = new List<Guid>();
                await using (var query = new NpgsqlCommand("SELECT seat_id FROM seat_allocations WHERE order_item_id = @id AND status = 'Sold' ORDER BY seat_id", connection, tx))
                {
                    query.Parameters.AddWithValue("id", item.Id);
                    await using var reader = await query.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) seats.Add(reader.GetGuid(0));
                }
                if (item.Mode == "ReservedSeating" && seats.Count != item.Quantity)
                    throw new PoisonMessageException("Sold seat allocation count is inconsistent.");
                for (int ordinal = 1; ordinal <= item.Quantity; ordinal++)
                {
                    Guid ticketId = Guid.NewGuid();
                    string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                    var signer = qrTokens ?? new QrTokens();
                    string token = signer.Create(ticketId, nonce, signer.ActiveKeyId);
                    await using var insert = new NpgsqlCommand("""
                        INSERT INTO tickets(id, order_item_id, user_id, event_session_id, seat_id,
                            ticket_code, qr_token_hash, qr_nonce, qr_key_id, ordinal)
                        VALUES (@id, @item, @user, @session, @seat, @code, @hash, @nonce, @key, @ordinal)
                        ON CONFLICT(order_item_id, ordinal) DO NOTHING
                        """, connection, tx);
                    insert.Parameters.AddWithValue("id", ticketId);
                    insert.Parameters.AddWithValue("item", item.Id);
                    insert.Parameters.AddWithValue("user", userId);
                    insert.Parameters.AddWithValue("session", sessionId);
                    insert.Parameters.AddWithValue("seat", NpgsqlTypes.NpgsqlDbType.Uuid,
                        item.Mode == "ReservedSeating" ? seats[ordinal - 1] : DBNull.Value);
                    insert.Parameters.AddWithValue("code", $"TF-{ticketId:N}".ToUpperInvariant());
                    insert.Parameters.AddWithValue("hash", HashToken(token));
                    insert.Parameters.AddWithValue("nonce", nonce);
                    insert.Parameters.AddWithValue("key", signer.ActiveKeyId);
                    insert.Parameters.AddWithValue("ordinal", ordinal);
                    await insert.ExecuteNonQueryAsync(ct);
                }
            }
            await using var notify = new NpgsqlCommand("""
                INSERT INTO outbox_messages(id, type, payload, occurred_at_utc, correlation_id)
                VALUES (@id, 'TicketsIssued', jsonb_build_object('orderId', @order), clock_timestamp(), @correlation)
                """, connection, tx);
            notify.Parameters.AddWithValue("id", Guid.NewGuid());
            notify.Parameters.AddWithValue("order", orderId);
            notify.Parameters.AddWithValue("correlation", message.CorrelationId);
            await notify.ExecuteNonQueryAsync(ct);
        }
        else
        {
            // A durable mock effect, not an actual email. Uniqueness covers even new message IDs.
            foreach (string channel in new[] { "Email", "InApp" })
            {
                await using var notify = new NpgsqlCommand("""
                    INSERT INTO mock_notifications(id, order_id, channel, correlation_id)
                    VALUES (@id, @order, @channel, @correlation) ON CONFLICT(order_id, channel) DO NOTHING
                    """, connection, tx);
                notify.Parameters.AddWithValue("id", Guid.NewGuid());
                notify.Parameters.AddWithValue("order", orderId);
                notify.Parameters.AddWithValue("channel", channel);
                notify.Parameters.AddWithValue("correlation", message.CorrelationId);
                await notify.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
    }
}
