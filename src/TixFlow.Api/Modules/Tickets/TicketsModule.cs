using Microsoft.EntityFrameworkCore;
using Npgsql;
using QRCoder;
using System.Security.Cryptography;
using System.Text.Json;
using TixFlow.Api.Authentication;
using TixFlow.Domain.Identity;
using TixFlow.Domain.Orders;
using TixFlow.Infrastructure.Persistence;
using TixFlow.Infrastructure.Processing;

namespace TixFlow.Api.Modules.Tickets;

public static class TicketsModule
{
    public static IEndpointRouteBuilder MapTicketsModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/tickets", ListAsync).RequireAuthorization("CustomerOrAdmin");
        endpoints.MapGet("/tickets/{ticketId:guid}", GetAsync).RequireAuthorization("CustomerOrAdmin");
        endpoints.MapGet("/tickets/{ticketId:guid}/qr", GetQrAsync).RequireAuthorization("CustomerOrAdmin");
        endpoints.MapPost("/tickets/check-ins", CheckInAsync).RequireAuthorization("OrganizerOrAdmin");
        endpoints.MapPost("/check-ins", CheckInAsync).RequireAuthorization("OrganizerOrAdmin");
        return endpoints;
    }

    private static IQueryable<Ticket> Owned(TixFlowDbContext db, HttpContext context)
    {
        var user = (User)context.Items[LocalUserMiddleware.UserKey]!;
        context.Response.Headers.CacheControl = "no-store";
        // Tickets are personal, including when an Admin buys tickets.
        return db.Tickets.AsNoTracking().Where(x => x.UserId == user.Id);
    }

    private static async Task<IResult> ListAsync(HttpContext context, TixFlowDbContext db, Guid? orderId,
        CancellationToken ct)
    {
        var query = Owned(db, context);
        if (orderId.HasValue) query = query.Where(x => x.OrderItem.OrderId == orderId);
        var rows = await Details(query.OrderByDescending(x => x.IssuedAtUtc).ThenBy(x => x.Id).Take(500)).ToArrayAsync(ct);
        return Results.Ok(new { data = rows });
    }

    private static async Task<IResult> GetAsync(Guid ticketId, HttpContext context, TixFlowDbContext db, CancellationToken ct)
    {
        var ticket = await Details(Owned(db, context).Where(x => x.Id == ticketId)).SingleOrDefaultAsync(ct);
        return ticket is null ? Error(404, "RESOURCE_NOT_FOUND") : Results.Ok(new { data = ticket });
    }

    private static IQueryable<TicketView> Details(IQueryable<Ticket> query) => query.Select(x => new TicketView(
        x.Id, x.TicketCode, x.OrderItem.OrderId, x.UserId, x.EventSession.Event.Slug, x.EventSession.Event.Name,
        x.EventSession.StartsAtUtc, x.EventSession.EndsAtUtc,
        x.EventSession.Event.Venue == null ? "" : x.EventSession.Event.Venue.Name,
        x.OrderItem.TicketTypeNameSnapshot, x.Status.ToString(), x.CheckedInAtUtc,
        x.Seat == null ? null : x.Seat.Section + " / " + x.Seat.RowLabel + " / " + x.Seat.SeatNumber));

    private static async Task<IResult> GetQrAsync(Guid ticketId, string? supportReason, HttpContext context,
        TixFlowDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var user = (User)context.Items[LocalUserMiddleware.UserKey]!;
        context.Response.Headers.CacheControl = "no-store";
        bool admin = user.Role == UserRole.Admin;
        if (admin && (string.IsNullOrWhiteSpace(supportReason) || supportReason.Length > 1000))
            return Error(400, "SUPPORT_REASON_REQUIRED");
        var ticket = await db.Tickets.AsNoTracking().Where(x => x.Id == ticketId && (x.UserId == user.Id || admin))
            .Select(x => new { x.Id, x.QrNonce, x.QrKeyId, x.QrTokenHash, x.Status,
                OrderStatus = x.OrderItem.Order.Status, EventStatus = x.EventSession.Event.Status, SessionStatus = x.EventSession.Status }).SingleOrDefaultAsync(ct);
        if (ticket is null) return Error(404, "RESOURCE_NOT_FOUND");
        if (ticket.Status is TicketStatus.Cancelled or TicketStatus.Refunded || ticket.OrderStatus != OrderStatus.Paid
            || ticket.EventStatus != TixFlow.Domain.Events.EventStatus.Published
            || ticket.SessionStatus is TixFlow.Domain.Events.SessionStatus.Cancelled or TixFlow.Domain.Events.SessionStatus.Completed)
            return Error(409, "TICKET_INVALID");
        if (ticket.QrNonce is null || ticket.QrKeyId is null) return Error(409, "QR_NOT_AVAILABLE");
        string token = new QrTokens(configuration).Create(ticket.Id, ticket.QrNonce, ticket.QrKeyId);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(ticket.QrTokenHash),
            Convert.FromHexString(TicketDelivery.HashToken(token)))) return Error(503, "QR_KEY_UNAVAILABLE");
        if (admin)
        {
            db.AuditLogs.Add(new TixFlow.Domain.Common.AuditLog(user.Id, "TicketQrViewed", "Ticket", ticket.Id.ToString(),
                JsonSerializer.Serialize(new { reason = supportReason }), context.Connection.RemoteIpAddress?.ToString()));
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(new { data = new { qrToken = token, qrImageDataUrl = "data:image/png;base64," +
            Convert.ToBase64String(PngByteQRCodeHelper.GetQRCode(token, QRCodeGenerator.ECCLevel.M, 5)) } });
    }

    private static async Task<IResult> CheckInAsync(CheckInRequest request, HttpContext context,
        IConfiguration configuration, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        string key = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (key.Length is < 1 or > 120 || key.Any(c => c < '!' || c > '~')) return Error(400, "IDEMPOTENCY_KEY_REQUIRED");
        var user = (User)context.Items[LocalUserMiddleware.UserKey]!;
        bool manual = !string.IsNullOrWhiteSpace(request.TicketCode);
        if (request.EventSessionId == Guid.Empty || manual == !string.IsNullOrWhiteSpace(request.QrToken))
            return Error(400, "INVALID_CHECK_IN_REQUEST");
        if (!manual && (request.QrToken!.Length != 68 || !request.QrToken.StartsWith("tf1_", StringComparison.Ordinal)))
            return Error(400, "INVALID_QR_TOKEN");
        if (request.TicketCode?.Length > 50 || request.Reason?.Length > 1000
            || ((manual || user.Role == UserRole.Admin) && string.IsNullOrWhiteSpace(request.Reason)))
            return Error(400, "REASON_REQUIRED");
        string requestHash = TicketDelivery.HashToken(JsonSerializer.Serialize(new
        {
            request.EventSessionId, tokenHash = manual ? null : TicketDelivery.HashToken(request.QrToken!),
            ticketCode = request.TicketCode?.Trim().ToUpperInvariant(), reason = request.Reason?.Trim()
        }));
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        Guid ticketId, eventId, sessionId, orderId;
        await using (var reference = new NpgsqlCommand("""
            SELECT t.id, e.id, s.id, o.id FROM tickets t
            JOIN event_sessions s ON s.id = t.event_session_id JOIN events e ON e.id = s.event_id
            JOIN organizers org ON org.id = e.organizer_id
            JOIN order_items i ON i.id = t.order_item_id JOIN orders o ON o.id = i.order_id
            WHERE ((@manual AND t.ticket_code = @code) OR (NOT @manual AND t.qr_token_hash = @hash))
              AND t.event_session_id = @session AND (@admin OR org.owner_user_id = @user)
            """, connection, tx))
        {
            reference.Parameters.AddWithValue("hash", manual ? "" : TicketDelivery.HashToken(request.QrToken!));
            reference.Parameters.AddWithValue("manual", manual);
            reference.Parameters.AddWithValue("code", request.TicketCode?.Trim().ToUpperInvariant() ?? "");
            reference.Parameters.AddWithValue("session", request.EventSessionId);
            reference.Parameters.AddWithValue("admin", user.Role == UserRole.Admin);
            reference.Parameters.AddWithValue("user", user.Id);
            await using var reader = await reference.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return Error(404, "RESOURCE_NOT_FOUND");
            ticketId = reader.GetGuid(0); eventId = reader.GetGuid(1); sessionId = reader.GetGuid(2); orderId = reader.GetGuid(3);
        }
        // Serialize key reuse even if a scanner submits two different ticket tokens.
        await using (var keyLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, tx))
        {
            keyLock.Parameters.AddWithValue("key", $"check-in:{user.Id}:{key}");
            await keyLock.ExecuteNonQueryAsync(ct);
        }
        await using (var replay = new NpgsqlCommand("SELECT ticket_id, checked_in_at_utc, request_hash FROM ticket_check_ins WHERE actor_user_id = @user AND idempotency_key = @key", connection, tx))
        {
            replay.Parameters.AddWithValue("user", user.Id); replay.Parameters.AddWithValue("key", key);
            await using var reader = await replay.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetGuid(0) != ticketId || reader.IsDBNull(2) || reader.GetString(2) != requestHash)
                    return Error(409, "IDEMPOTENCY_KEY_REUSED");
                context.Response.Headers["Idempotency-Replayed"] = "true";
                return Results.Ok(new { data = new { ticketId, status = "Used", checkedInAtUtc = reader.GetFieldValue<DateTimeOffset>(1) } });
            }
        }
        string? eventStatus = await State("SELECT status FROM events WHERE id = @id FOR UPDATE", eventId);
        string? sessionStatus = await State("SELECT status FROM event_sessions WHERE id = @id FOR UPDATE", sessionId);
        string? orderStatus = await State("SELECT status FROM orders WHERE id = @id FOR UPDATE", orderId);
        string? ticketStatus = await State("SELECT status FROM tickets WHERE id = @id FOR UPDATE", ticketId);
        if (ticketStatus == "Used") return Error(409, "TICKET_ALREADY_USED");
        if (ticketStatus != "Issued" || orderStatus != "Paid" || eventStatus != "Published"
            || sessionStatus is "Cancelled" or "Completed") return Error(409, "TICKET_INVALID");
        await using (var window = new NpgsqlCommand("""
            SELECT clock_timestamp() >= starts_at_utc - interval '60 minutes' AND clock_timestamp() < ends_at_utc
            FROM event_sessions WHERE id = @id
            """, connection, tx))
        {
            window.Parameters.AddWithValue("id", sessionId);
            if (!Equals(await window.ExecuteScalarAsync(ct), true)) return Error(409, "CHECK_IN_WINDOW_CLOSED");
        }
        await using var update = new NpgsqlCommand("""
            WITH used AS (
                UPDATE tickets SET status = 'Used', checked_in_at_utc = clock_timestamp()
                WHERE id = @ticket AND status = 'Issued' RETURNING id, checked_in_at_utc
            )
            INSERT INTO ticket_check_ins(ticket_id, actor_user_id, idempotency_key, checked_in_at_utc, request_hash)
            SELECT id, @user, @key, checked_in_at_utc, @hash FROM used RETURNING checked_in_at_utc
            """, connection, tx);
        update.Parameters.AddWithValue("ticket", ticketId); update.Parameters.AddWithValue("user", user.Id); update.Parameters.AddWithValue("key", key);
        update.Parameters.AddWithValue("hash", requestHash);
        var checkedIn = new DateTimeOffset((DateTime)(await update.ExecuteScalarAsync(ct))!);
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO audit_logs(id, actor_user_id, action, entity_type, entity_id, data)
            VALUES (@id, @actor, 'TicketCheckedIn', 'Ticket', @ticket, @metadata::jsonb)
            """, connection, tx))
        {
            audit.Parameters.AddWithValue("id", Guid.NewGuid()); audit.Parameters.AddWithValue("actor", user.Id);
            audit.Parameters.AddWithValue("ticket", ticketId.ToString());
            audit.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new { method = manual ? "Manual" : "Qr", reason = request.Reason, eventSessionId = sessionId }));
            await audit.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Results.Ok(new { data = new { ticketId, status = "Used", checkedInAtUtc = checkedIn } });

        async Task<string?> State(string sql, Guid id)
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            command.Parameters.AddWithValue("id", id);
            return (string?)await command.ExecuteScalarAsync(ct);
        }
    }

    private static IResult Error(int status, string code) => Results.Problem(statusCode: status,
        title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
    public sealed record CheckInRequest(Guid EventSessionId, string? QrToken = null, string? TicketCode = null, string? Reason = null);
    public sealed record TicketView(Guid Id, string TicketCode, Guid OrderId, Guid OwnerId, string EventSlug,
        string EventTitle, DateTimeOffset EventDate, DateTimeOffset EventEndsAtUtc, string Venue, string TicketType,
        string Status, DateTimeOffset? CheckedInAtUtc, string? SeatLabel);
}
