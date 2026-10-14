using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TixFlow.Api.Authentication;
using TixFlow.Domain.Booking;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;
using TixFlow.Domain.Messaging;
using TixFlow.Domain.Orders;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.Orders;

public static class OrdersModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static IEndpointRouteBuilder MapOrdersModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/holds/{holdId:guid}/checkout", CheckoutAsync).RequireAuthorization("Customer");
        endpoints.MapGet("/orders", ListOrdersAsync).RequireAuthorization("CustomerOrAdmin");
        endpoints.MapGet("/orders/{orderId:guid}", GetOrderAsync).RequireAuthorization("CustomerOrAdmin");
        endpoints.MapPost("/orders/{orderId:guid}/payments/mock", CreateMockPaymentAsync).RequireAuthorization("Customer");
        endpoints.MapGet("/payments/{paymentId:guid}", GetPaymentAsync).RequireAuthorization("CustomerOrAdmin");
        endpoints.MapPost("/payment-webhooks/mock", ReceiveMockWebhookAsync).RequireAuthorization("MockPaymentService");
        return endpoints;
    }

    private static Task<IResult> CheckoutAsync(Guid holdId, HttpContext context, TixFlowDbContext db, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() =>
        {
            db.ChangeTracker.Clear();
            return CheckoutCoreAsync(holdId, context, db, ct);
        });

    private static async Task<IResult> CheckoutCoreAsync(Guid holdId, HttpContext context, TixFlowDbContext db,
        CancellationToken ct)
    {
        User user = CurrentUser(context);
        context.Response.Headers.CacheControl = "no-store";
        if (!TryKey(context, out string key)) return Problem(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "A valid Idempotency-Key is required.");
        bool admin = user.Role == UserRole.Admin;
        var reference = await db.Holds.AsNoTracking().Where(x => x.Id == holdId && (admin || x.UserId == user.Id))
            .Select(x => new { x.Id, x.EventSessionId, x.UserId }).SingleOrDefaultAsync(ct);
        if (reference is null) return NotFound(context);

        string scope = $"checkout:{holdId:N}";
        IdempotencyRecord? replayRecord = await db.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.Scope == scope && x.Key == key, ct);
        long expected = 0;
        string requestHash = string.Empty;
        if (replayRecord is not null)
        {
            if (!TryVersion(context, out expected)) return Problem(context, 400, "EXPECTED_VERSION_REQUIRED", "A valid X-Expected-Version is required.");
            requestHash = Sha256($"{holdId:N}:{expected}");
            IResult? replay = ReplayOrConflict(replayRecord, requestHash, context);
            if (replay is not null) return replay;
        }

        Order? existing = await db.Orders.Include(x => x.Items).AsNoTracking().SingleOrDefaultAsync(x => x.HoldId == holdId, ct);
        if (existing is not null) return Json(context, new { data = await OrderDto(db, existing.Id, ct) }, 200);
        if (!TryVersion(context, out expected)) return Problem(context, 400, "EXPECTED_VERSION_REQUIRED", "A valid X-Expected-Version is required.");
        requestHash = Sha256($"{holdId:N}:{expected}");

        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);
        var sessionRef = await db.EventSessions.AsNoTracking().Where(x => x.Id == reference.EventSessionId)
            .Select(x => new { x.Id, x.EventId }).SingleAsync(ct);
        Event? eventItem = await db.Events.FromSqlInterpolated($"SELECT * FROM events WHERE id = {sessionRef.EventId} FOR UPDATE").SingleOrDefaultAsync(ct);
        EventSession? session = await db.EventSessions.FromSqlInterpolated($"SELECT * FROM event_sessions WHERE id = {sessionRef.Id} FOR UPDATE").SingleOrDefaultAsync(ct);
        Hold? hold = await db.Holds.FromSqlInterpolated($"SELECT * FROM holds WHERE id = {holdId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (hold is null || (hold.UserId != user.Id && !admin)) return NotFound(context);
        replayRecord = await db.IdempotencyRecords.SingleOrDefaultAsync(
            x => x.UserId == user.Id && x.Scope == scope && x.Key == key, ct);
        if (replayRecord is not null)
        {
            IResult? replay = ReplayOrConflict(replayRecord, requestHash, context);
            if (replay is not null) return replay;
        }
        existing = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.HoldId == holdId, ct);
        if (existing is not null)
        {
            await tx.CommitAsync(ct);
            return Json(context, new { data = await OrderDto(db, existing.Id, ct) }, 200);
        }
        if (hold.Version != expected) return Problem(context, 409, "VERSION_CONFLICT", "The Hold version has changed.");
        DateTimeOffset now = await DbNow(db, ct);
        if (hold.Status != HoldStatus.Active || now >= hold.ExpiresAtUtc)
            return Problem(context, 409, "HOLD_EXPIRED", "Only an active Hold before its deadline can be checked out.");
        if (eventItem?.Status != EventStatus.Published || session is null || session.Status is SessionStatus.Cancelled or SessionStatus.Completed)
            return Problem(context, 409, "EVENT_CANCELLED", "The event or session is no longer available.");

        List<HoldItem> items = await db.HoldItems.Where(x => x.HoldId == holdId).OrderBy(x => x.Id).ToListAsync(ct);
        if (items.Count == 0) return Problem(context, 409, "INVALID_STATE", "The Hold has no items.");
        string orderNumber = $"TF-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..22].ToUpperInvariant();
        var order = new Order(user.Id, hold.EventSessionId, hold.Id, orderNumber, hold.TotalAmount);
        var ledger = new IdempotencyRecord(user.Id, scope, key, requestHash, now.AddHours(24));
        db.IdempotencyRecords.Add(ledger);
        db.Orders.Add(order);
        var itemLinks = new Dictionary<Guid, OrderItem>();
        foreach (HoldItem item in items)
        {
            var orderItem = new OrderItem(order.Id, item.TicketTypeId, item.Quantity, item.UnitPrice,
                item.TicketTypeNameSnapshot, item.ServiceFee);
            db.OrderItems.Add(orderItem);
            itemLinks[item.Id] = orderItem;
        }
        await db.SaveChangesAsync(ct);
        foreach (var pair in itemLinks)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE seat_allocations SET order_item_id = {pair.Value.Id}, updated_at_utc = {now}
                WHERE hold_item_id = {pair.Key} AND status = 'Held'
                """, ct);
        string responseBody = JsonSerializer.Serialize(new { data = await OrderDto(db, order.Id, ct) }, JsonOptions);
        ledger.Complete(201, responseBody, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        context.Response.Headers.Location = $"/api/v1/orders/{order.Id}";
        return JsonText(responseBody, 201);
    }

    private static async Task<IResult> ListOrdersAsync(HttpContext context, TixFlowDbContext db, CancellationToken ct)
    {
        User user = CurrentUser(context);
        context.Response.Headers.CacheControl = "no-store";
        bool admin = user.Role == UserRole.Admin;
        Guid[] ids = await db.Orders.AsNoTracking().Where(x => admin || x.UserId == user.Id)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => x.Id).ToArrayAsync(ct);
        var data = new List<object>(ids.Length);
        foreach (Guid id in ids) data.Add(await OrderDto(db, id, ct));
        context.Response.Headers.CacheControl = "no-store";
        return Json(context, new { data, page = new { limit = 100, nextCursor = (string?)null } }, 200);
    }

    private static async Task<IResult> GetOrderAsync(Guid orderId, HttpContext context, TixFlowDbContext db, CancellationToken ct)
    {
        User user = CurrentUser(context);
        bool exists = await db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && (user.Role == UserRole.Admin || x.UserId == user.Id), ct);
        if (!exists) return NotFound(context);
        context.Response.Headers.CacheControl = "no-store";
        return Json(context, new { data = await OrderDto(db, orderId, ct) }, 200);
    }

    private static Task<IResult> CreateMockPaymentAsync(Guid orderId, MockPaymentRequest request,
        HttpContext context, TixFlowDbContext db, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() =>
        {
            db.ChangeTracker.Clear();
            return CreateMockPaymentCoreAsync(orderId, request, context, db, ct);
        });

    private static async Task<IResult> CreateMockPaymentCoreAsync(Guid orderId, MockPaymentRequest request,
        HttpContext context, TixFlowDbContext db, CancellationToken ct)
    {
        User user = CurrentUser(context);
        context.Response.Headers.CacheControl = "no-store";
        if (!TryKey(context, out string key)) return Problem(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "A valid Idempotency-Key is required.");
        if (!Enum.TryParse(request.Outcome, true, out MockOutcome outcome) || !Enum.IsDefined(outcome))
            return Problem(context, 422, "VALIDATION_ERROR", "Outcome must be Succeeded, Failed, or Timeout.");
        Order? order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId && x.UserId == user.Id, ct);
        if (order is null) return NotFound(context);
        string digest = Sha256($"{user.Id:N}:createMockPayment:{orderId:N}:{key}");
        string scope = $"createMockPayment:{orderId:N}";
        IdempotencyRecord? previous = await db.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.Scope == scope && x.Key == key, ct);
        long expected = 0;
        string requestHash = string.Empty;
        if (previous is not null)
        {
            if (!TryVersion(context, out expected)) return Problem(context, 400, "EXPECTED_VERSION_REQUIRED", "A valid X-Expected-Version is required.");
            requestHash = Sha256($"{orderId:N}:{outcome}:{expected}");
            IResult? replay = ReplayOrConflict(previous, requestHash, context);
            if (replay is not null) return replay;
        }
        if (!TryVersion(context, out expected)) return Problem(context, 400, "EXPECTED_VERSION_REQUIRED", "A valid X-Expected-Version is required.");
        requestHash = Sha256($"{orderId:N}:{outcome}:{expected}");
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);
        order = await db.Orders.FromSqlInterpolated($"SELECT * FROM orders WHERE id = {orderId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (order is null || order.UserId != user.Id) return NotFound(context);
        previous = await db.IdempotencyRecords.SingleOrDefaultAsync(
            x => x.UserId == user.Id && x.Scope == scope && x.Key == key, ct);
        if (previous is not null)
        {
            IResult? replay = ReplayOrConflict(previous, requestHash, context);
            if (replay is not null) return replay;
        }
        if (order.Version != expected) return Problem(context, 409, "VERSION_CONFLICT", "The Order version has changed.");
        if (order.Status != OrderStatus.PendingPayment) return Problem(context, 409, "PAYMENT_ALREADY_SUCCEEDED", "This order is no longer payable.");
        if (await db.Payments.AnyAsync(x => x.OrderId == orderId && x.Status == PaymentStatus.Pending, ct))
            return Problem(context, 409, "PAYMENT_IN_PROGRESS", "A payment attempt is already pending.");
        DateTimeOffset now = await DbNow(db, ct);
        Guid paymentId = Guid.NewGuid();
        var ledger = new IdempotencyRecord(user.Id, scope, key, requestHash, now.AddHours(24));
        db.IdempotencyRecords.Add(ledger);
        // Keep the request outcome with the attempt and enqueue only after all database work commits.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payments (id, order_id, idempotency_key, method, status, amount, currency, mock_outcome, created_at_utc, updated_at_utc)
            VALUES ({paymentId}, {orderId}, {digest}, 'Mock', 'Pending', {order.TotalAmount}, {order.Currency}, {outcome.ToString()}, {now}, {now})
            """, ct);
        string payload = JsonSerializer.Serialize(new MockPaymentJob(paymentId, outcome.ToString()), JsonOptions);
        db.OutboxMessages.Add(new OutboxMessage("MockPaymentRequested", payload, now));
        await db.SaveChangesAsync(ct);
        string responseBody = JsonSerializer.Serialize(new { data = await PaymentDto(db, paymentId, ct) }, JsonOptions);
        ledger.Complete(201, responseBody, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        context.Response.Headers.Location = $"/api/v1/payments/{paymentId}";
        return JsonText(responseBody, 201);
    }

    private static async Task<IResult> GetPaymentAsync(Guid paymentId, HttpContext context, TixFlowDbContext db, CancellationToken ct)
    {
        User user = CurrentUser(context);
        bool authorized = await db.Payments.AsNoTracking().AnyAsync(x => x.Id == paymentId &&
            (user.Role == UserRole.Admin || x.Order.UserId == user.Id), ct);
        if (!authorized) return NotFound(context);
        context.Response.Headers.CacheControl = "no-store";
        return Json(context, new { data = await PaymentDto(db, paymentId, ct) }, 200);
    }

    private static async Task<IResult> ReceiveMockWebhookAsync(MockPaymentCallback callback, HttpContext context,
        TixFlowDbContext db, CancellationToken ct)
    {
        SettlementReceipt result = await PaymentSettlement.ProcessAsync(db, callback, ct);
        return Json(context, new { data = result }, 200);
    }

    private static async Task<object> OrderDto(TixFlowDbContext db, Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Where(x => x.Id == id).Select(x => new
        {
            x.Id, x.OrderNumber, x.Status, x.TotalAmount, x.Currency, x.CreatedAtUtc, x.Version,
            x.Hold.ExpiresAtUtc, x.EventSession.Name, EventName = x.EventSession.Event.Name,
            LatestPaymentId = x.Payments.OrderByDescending(p => p.CreatedAtUtc).Select(p => (Guid?)p.Id).FirstOrDefault()
        }).SingleAsync(ct);
        var items = await db.OrderItems.AsNoTracking().Where(x => x.OrderId == id).Select(x => new
        {
            x.Id, x.TicketTypeId, x.TicketTypeNameSnapshot, x.Quantity, x.UnitPrice, x.ServiceFee,
            LineTotal = x.UnitPrice * x.Quantity + x.ServiceFee
        }).ToArrayAsync(ct);
        return new { order.Id, order.OrderNumber, order.Status, order.TotalAmount, order.Currency, order.CreatedAtUtc,
            order.Version, order.ExpiresAtUtc, EventSessionName = order.Name, order.EventName,
            order.LatestPaymentId, Items = items };
    }

    private static async Task<object> PaymentDto(TixFlowDbContext db, Guid id, CancellationToken ct) =>
        await db.Payments.AsNoTracking().Where(x => x.Id == id).Select(x => new
        {
            x.Id, x.OrderId, x.Status, x.Amount, x.Currency, x.ProviderTransactionId,
            x.FailureReason, x.CreatedAtUtc, x.PaidAtUtc
        }).SingleAsync(ct);

    private static User CurrentUser(HttpContext context) => context.Items[LocalUserMiddleware.UserKey] as User
        ?? throw new InvalidOperationException("The authorized local user is unavailable.");

    private static bool TryKey(HttpContext context, out string key)
    {
        key = context.Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty;
        return key.Length is >= 1 and <= 120 && key.All(c => c is >= '!' and <= '~');
    }

    private static IResult? ReplayOrConflict(IdempotencyRecord record, string requestHash, HttpContext context)
    {
        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            return Problem(context, 409, "IDEMPOTENCY_KEY_REUSED", "This key was used for a different request.");
        if (!record.IsCompleted || record.ResponseStatusCode is null || record.ResponseBody is null)
            return Problem(context, 409, "REQUEST_IN_PROGRESS", "A request with this key is still in progress.");
        context.Response.Headers["Idempotency-Replayed"] = "true";
        context.Response.Headers.CacheControl = "no-store";
        return JsonText(record.ResponseBody, record.ResponseStatusCode.Value);
    }

    private static bool TryVersion(HttpContext context, out long version) =>
        long.TryParse(context.Request.Headers["X-Expected-Version"].FirstOrDefault(), NumberStyles.None,
            CultureInfo.InvariantCulture, out version) && version >= 0;

    private static async Task<DateTimeOffset> DbNow(TixFlowDbContext db, CancellationToken ct) =>
        await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static IResult Json(HttpContext context, object value, int status) =>
        Results.Text(JsonSerializer.Serialize(value, JsonOptions), "application/json", Encoding.UTF8, status);
    private static IResult JsonText(string body, int status) =>
        Results.Text(body, "application/json", Encoding.UTF8, status);
    private static IResult Problem(HttpContext context, int status, string code, string detail) => Results.Json(new
    {
        type = $"/problems/{code.ToLowerInvariant().Replace('_', '-')}", title = code, status, detail,
        instance = context.Request.Path.Value, code, traceId = context.TraceIdentifier, retryable = false
    }, statusCode: status, contentType: "application/problem+json");
    private static IResult NotFound(HttpContext context) => Problem(context, 404, "RESOURCE_NOT_FOUND", "The requested resource was not found.");

    public sealed record MockPaymentRequest(string Outcome);
    public sealed record MockPaymentCallback(string ProviderEventId, Guid PaymentId, string ProviderTransactionId,
        string Outcome, decimal Amount, string Currency, DateTimeOffset OccurredAtUtc, string? FailureReason = null);
    public sealed record SettlementReceipt(string Disposition, string? Reason);
    public sealed record MockPaymentJob(Guid PaymentId, string Outcome);
    public enum MockOutcome { Succeeded, Failed, Timeout }
}

public static class PaymentSettlement
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task<OrdersModule.SettlementReceipt> ProcessAsync(TixFlowDbContext db,
        OrdersModule.MockPaymentCallback callback, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() =>
        {
            db.ChangeTracker.Clear();
            return ProcessCoreAsync(db, callback, ct);
        });

    private static async Task<OrdersModule.SettlementReceipt> ProcessCoreAsync(TixFlowDbContext db,
        OrdersModule.MockPaymentCallback callback, CancellationToken ct)
    {
        if (callback.Outcome is not ("Succeeded" or "Failed") || string.IsNullOrWhiteSpace(callback.ProviderEventId)
            || callback.ProviderEventId.Length > 200 || string.IsNullOrWhiteSpace(callback.ProviderTransactionId)
            || callback.ProviderTransactionId.Length > 200 || callback.Amount < 0
            || string.IsNullOrWhiteSpace(callback.Currency) || callback.Currency.Length != 3
            || callback.FailureReason?.Length > 80)
            throw new ArgumentException("Malformed payment callback.");
        string payload = JsonSerializer.Serialize(callback, JsonOptions);
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);
        int inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payment_webhook_events (id, provider, provider_event_id, payload, received_at_utc)
            VALUES ({Guid.NewGuid()}, 'mock', {callback.ProviderEventId}, CAST({payload} AS jsonb), clock_timestamp())
            ON CONFLICT (provider, provider_event_id) DO NOTHING
            """, ct);
        if (inserted == 0)
        {
            string? old = await db.PaymentWebhookEvents.AsNoTracking().Where(x => x.Provider == "mock" && x.ProviderEventId == callback.ProviderEventId)
                .Select(x => x.Payload).SingleAsync(ct);
            OrdersModule.MockPaymentCallback? previous = old is null
                ? null : JsonSerializer.Deserialize<OrdersModule.MockPaymentCallback>(old, JsonOptions);
            if (previous != callback)
                return new("Ignored", "WEBHOOK_EVENT_CONFLICT");
            return new("Duplicate", null);
        }
        var reference = await db.Payments.AsNoTracking().Where(x => x.Id == callback.PaymentId)
            .Select(x => new { x.OrderId, x.Order.HoldId, x.Order.EventSessionId }).SingleOrDefaultAsync(ct);
        if (reference is null) return await Finish(db, tx, callback.ProviderEventId, "Ignored", "PAYMENT_NOT_FOUND", ct);
        var sessionRef = await db.EventSessions.AsNoTracking().Where(x => x.Id == reference.EventSessionId)
            .Select(x => new { x.EventId }).SingleAsync(ct);
        Event? eventItem = await db.Events.FromSqlInterpolated($"SELECT * FROM events WHERE id = {sessionRef.EventId} FOR UPDATE").SingleAsync(ct);
        EventSession? session = await db.EventSessions.FromSqlInterpolated($"SELECT * FROM event_sessions WHERE id = {reference.EventSessionId} FOR UPDATE").SingleAsync(ct);
        Hold? hold = await db.Holds.FromSqlInterpolated($"SELECT * FROM holds WHERE id = {reference.HoldId} FOR UPDATE").SingleAsync(ct);
        Order? order = await db.Orders.FromSqlInterpolated($"SELECT * FROM orders WHERE id = {reference.OrderId} FOR UPDATE").SingleAsync(ct);
        Payment? payment = await db.Payments.FromSqlInterpolated($"SELECT * FROM payments WHERE id = {callback.PaymentId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (payment is null) return await Finish(db, tx, callback.ProviderEventId, "Ignored", "PAYMENT_NOT_FOUND", ct);
        if (payment.Status != PaymentStatus.Pending || order.Status != OrderStatus.PendingPayment)
            return await Finish(db, tx, callback.ProviderEventId, "Ignored", "PAYMENT_NOT_PENDING", ct);
        if (payment.Amount != callback.Amount || !string.Equals(payment.Currency, callback.Currency, StringComparison.Ordinal))
            return await Finish(db, tx, callback.ProviderEventId, "Ignored", "PAYMENT_AMOUNT_MISMATCH", ct);
        DateTimeOffset now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        bool expired = now >= hold.ExpiresAtUtc || hold.Status != HoldStatus.Active;
        bool cancelled = eventItem.Status != EventStatus.Published || session.Status is SessionStatus.Cancelled or SessionStatus.Completed;
        if (expired || cancelled)
        {
            string reason = expired ? "ORDER_EXPIRED" : "EVENT_CANCELLED";
            await ReleaseHold(db, hold, order, payment, now, reason, ct);
            return await Finish(db, tx, callback.ProviderEventId, "Ignored", reason, ct);
        }
        if (callback.Outcome == "Failed")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE payments SET status = 'Failed', failure_reason = {callback.FailureReason ?? "MOCK_DECLINED"},
                    provider_transaction_id = {callback.ProviderTransactionId}, updated_at_utc = {now}
                WHERE id = {payment.Id} AND status = 'Pending'
                """, ct);
            return await Finish(db, tx, callback.ProviderEventId, "Processed", null, ct);
        }
        var held = await db.HoldItems.AsNoTracking().Where(x => x.HoldId == hold.Id)
            .Select(x => new { x.TicketTypeId, x.Quantity }).ToArrayAsync(ct);
        foreach (var item in held.OrderBy(x => x.TicketTypeId))
        {
            int changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE ticket_inventories SET held_quantity = held_quantity - {item.Quantity},
                    sold_quantity = sold_quantity + {item.Quantity}, version = version + 1, updated_at_utc = {now}
                WHERE ticket_type_id = {item.TicketTypeId} AND held_quantity >= {item.Quantity}
                """, ct);
            if (changed != 1) throw new InvalidOperationException("Held inventory invariant was violated during payment settlement.");
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE seat_allocations SET status = 'Sold', hold_item_id = NULL, expires_at_utc = NULL, updated_at_utc = {now}
            WHERE hold_item_id IN (SELECT id FROM hold_items WHERE hold_id = {hold.Id}) AND status = 'Held'
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE holds SET status = 'Confirmed', version = version + 1, updated_at_utc = {now}
            WHERE id = {hold.Id} AND status = 'Active'
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE orders SET status = 'Paid', version = version + 1, updated_at_utc = {now}
            WHERE id = {order.Id} AND status = 'PendingPayment'
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE payments SET status = 'Succeeded', provider_transaction_id = {callback.ProviderTransactionId},
                paid_at_utc = {now}, updated_at_utc = {now}
            WHERE id = {payment.Id} AND status = 'Pending'
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH requested AS (
                INSERT INTO ticket_issuance_requests(order_id) VALUES ({order.Id})
                ON CONFLICT DO NOTHING RETURNING order_id
            )
            INSERT INTO outbox_messages(id, type, payload, occurred_at_utc, correlation_id)
            SELECT {Guid.NewGuid()}, 'OrderPaid', jsonb_build_object('orderId', order_id), {now}, order_id::text
            FROM requested
            """, ct);
        return await Finish(db, tx, callback.ProviderEventId, "Processed", null, ct);
    }

    private static async Task ReleaseHold(TixFlowDbContext db, Hold hold, Order order, Payment payment,
        DateTimeOffset now, string reason, CancellationToken ct)
    {
        var held = await db.HoldItems.AsNoTracking().Where(x => x.HoldId == hold.Id && hold.Status == HoldStatus.Active)
            .Select(x => new { x.TicketTypeId, x.Quantity }).ToArrayAsync(ct);
        foreach (var item in held.OrderBy(x => x.TicketTypeId))
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE ticket_inventories SET held_quantity = held_quantity - {item.Quantity},
                    version = version + 1, updated_at_utc = {now} WHERE ticket_type_id = {item.TicketTypeId}
                """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE seat_allocations SET status = 'Released',
                expires_at_utc = NULL, updated_at_utc = {now}
            WHERE hold_item_id IN (SELECT id FROM hold_items WHERE hold_id = {hold.Id}) AND status = 'Held'
            """, ct);
        string holdStatus = reason == "ORDER_EXPIRED" ? "Expired" : "Cancelled";
        string orderStatus = reason == "ORDER_EXPIRED" ? "Expired" : "Cancelled";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE holds SET status = {holdStatus}, version = version + 1, updated_at_utc = {now} WHERE id = {hold.Id} AND status = 'Active'", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders SET status = {orderStatus}, version = version + 1, updated_at_utc = {now} WHERE id = {order.Id} AND status = 'PendingPayment'", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE payments SET status = 'Failed', failure_reason = {reason}, updated_at_utc = {now} WHERE id = {payment.Id} AND status = 'Pending'", ct);
    }

    private static async Task<OrdersModule.SettlementReceipt> Finish(TixFlowDbContext db, IDbContextTransaction tx,
        string eventId, string disposition, string? reason, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE payment_webhook_events SET processed_at_utc = clock_timestamp(), disposition = {disposition}, reason = {reason}
            WHERE provider = 'mock' AND provider_event_id = {eventId}
            """, ct);
        await tx.CommitAsync(ct);
        return new(disposition, reason);
    }
}

public sealed class MockPaymentSimulator(IServiceScopeFactory scopeFactory, ILogger<MockPaymentSimulator> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                TixFlowDbContext db = scope.ServiceProvider.GetRequiredService<TixFlowDbContext>();
                var job = await db.OutboxMessages.AsNoTracking().Where(x => x.Type == "MockPaymentRequested" && x.ProcessedAtUtc == null)
                    .OrderBy(x => x.OccurredAtUtc).Select(x => new { x.Id, x.Payload }).FirstOrDefaultAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
                    continue;
                }
                OrdersModule.MockPaymentJob? parsed = JsonSerializer.Deserialize<OrdersModule.MockPaymentJob>(job.Payload,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (parsed is null) throw new InvalidOperationException("Malformed mock payment job.");
                await Task.Delay(TimeSpan.FromMilliseconds(400), stoppingToken);
                OrdersModule.MockOutcome outcome = Enum.Parse<OrdersModule.MockOutcome>(parsed.Outcome, true);
                {
                    Payment? payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parsed.PaymentId, stoppingToken);
                    if (payment is not null)
                    {
                        var callback = new OrdersModule.MockPaymentCallback($"mock-event-{parsed.PaymentId:N}", parsed.PaymentId,
                            $"mock-txn-{parsed.PaymentId:N}", outcome == OrdersModule.MockOutcome.Succeeded ? "Succeeded" : "Failed",
                            payment.Amount, payment.Currency, DateTimeOffset.UtcNow,
                            outcome == OrdersModule.MockOutcome.Timeout ? "MOCK_TIMEOUT" : "MOCK_DECLINED");
                        await PaymentSettlement.ProcessAsync(db, callback, stoppingToken);
                    }
                }
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET processed_at_utc = clock_timestamp(), attempt_count = attempt_count + 1 WHERE id = {job.Id}", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogWarning("Mock payment simulator will retry ({FailureType}).", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
