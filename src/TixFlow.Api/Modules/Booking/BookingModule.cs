using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using TixFlow.Api.Authentication;
using TixFlow.Domain.Booking;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.Booking;

public static class BookingModule
{
    private const string CreateHoldScope = "createHold:collection";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static IEndpointRouteBuilder MapBookingModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/bookings")
            .WithTags("Booking")
            .MapGet("/status", () => Results.Ok(new
            {
                module = "Booking",
                status = "hold-concurrency-ready",
                note = "Transactional hold, idempotency, atomic inventory and cancellation APIs are enabled."
            }));

        RouteGroupBuilder holds = endpoints.MapGroup("/holds").WithTags("Booking");
        holds.MapPost("/", CreateHoldAsync).RequireAuthorization("Customer");
        holds.MapGet("/", ListHoldsAsync).RequireAuthorization("CustomerOrAdmin");
        holds.MapGet("/{holdId:guid}", GetHoldAsync).RequireAuthorization("CustomerOrAdmin");
        holds.MapPost("/{holdId:guid}/cancellations", CancelHoldAsync)
            .RequireAuthorization("CustomerOrAdmin");
        holds.MapDelete("/{holdId:guid}", DeleteHoldAsync)
            .RequireAuthorization("CustomerOrAdmin");

        return endpoints;
    }

    private static async Task<IResult> CreateHoldAsync(
        CreateHoldRequest request,
        HttpContext context,
        TixFlowDbContext db,
        IOptions<BookingOptions> options,
        CancellationToken cancellationToken)
    {
        User user = CurrentUser(context);
        if (!TryReadIdempotencyKey(context, out string idempotencyKey, out IResult? keyError))
        {
            return keyError!;
        }

        CanonicalHoldRequest canonical;
        try
        {
            canonical = Canonicalize(request);
        }
        catch (BookingProblemException problem)
        {
            return Problem(context, problem);
        }

        string requestHash = HashCanonicalRequest(canonical);
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                return await CreateHoldAttemptAsync(
                    canonical,
                    user,
                    idempotencyKey,
                    requestHash,
                    context,
                    db,
                    options.Value,
                    cancellationToken);
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            context.Response.Headers.RetryAfter = "1";
            return Problem(context, new BookingProblemException(
                StatusCodes.Status409Conflict,
                "REQUEST_IN_PROGRESS",
                "A conflicting booking request is still in progress.",
                retryable: true));
        }
    }

    private static async Task<IResult> CreateHoldAttemptAsync(
        CanonicalHoldRequest request,
        User user,
        string idempotencyKey,
        string requestHash,
        HttpContext context,
        TixFlowDbContext db,
        BookingOptions options,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ConfigureTransactionAsync(db, options.LockTimeoutSeconds, cancellationToken);

        IdempotencyClaim claim = await ClaimIdempotencyAsync(
            db,
            user.Id,
            CreateHoldScope,
            idempotencyKey,
            requestHash,
            options.IdempotencyRetentionHours,
            cancellationToken);

        if (!claim.IsNew)
        {
            IResult replay = ReplayOrConflict(claim.Record, requestHash, context);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        await transaction.CreateSavepointAsync("booking_command", cancellationToken);
        try
        {
            Guid holdId = await ExecuteCreateHoldAsync(
                request,
                user,
                idempotencyKey,
                db,
                options,
                cancellationToken);

            HoldEnvelope response = new(await LoadHoldAsync(db, holdId, cancellationToken));
            string responseBody = JsonSerializer.Serialize(response, JsonOptions);
            DateTimeOffset completedAt = await DatabaseTimeAsync(db, cancellationToken);
            claim.Record.Complete(StatusCodes.Status201Created, responseBody, completedAt);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.ReleaseSavepointAsync("booking_command", cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            context.Response.Headers.Location = $"/api/v1/holds/{holdId}";
            context.Response.Headers.CacheControl = "no-store";
            return JsonText(responseBody, StatusCodes.Status201Created);
        }
        catch (BookingProblemException problem)
        {
            return await CommitBusinessProblemAsync(
                db, transaction, claim.Record.Id, problem, context, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsActiveSeatConflict(exception))
        {
            return await CommitBusinessProblemAsync(
                db,
                transaction,
                claim.Record.Id,
                new BookingProblemException(
                    StatusCodes.Status409Conflict,
                    "SEAT_UNAVAILABLE",
                    "One or more selected seats are no longer available."),
                context,
                cancellationToken);
        }
    }

    private static async Task<Guid> ExecuteCreateHoldAsync(
        CanonicalHoldRequest request,
        User user,
        string idempotencyKey,
        TixFlowDbContext db,
        BookingOptions options,
        CancellationToken cancellationToken)
    {
        SessionLockReference? sessionReference = await db.EventSessions
            .AsNoTracking()
            .Where(item => item.Id == request.EventSessionId)
            .Select(item => new SessionLockReference(item.Id, item.EventId))
            .SingleOrDefaultAsync(cancellationToken);
        if (sessionReference is null)
        {
            throw NotFound();
        }

        Event? eventItem = await db.Events
            .FromSqlInterpolated($"SELECT * FROM events WHERE id = {sessionReference.EventId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        EventSession? session = await db.EventSessions
            .FromSqlInterpolated($"SELECT * FROM event_sessions WHERE id = {request.EventSessionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (eventItem is null || session is null)
        {
            throw NotFound();
        }

        var ticketTypes = new Dictionary<Guid, TicketType>();
        var inventories = new Dictionary<Guid, TicketInventory>();
        foreach (Guid ticketTypeId in request.Items.Select(item => item.TicketTypeId).Order())
        {
            TicketType? ticketType = await db.TicketTypes
                .FromSqlInterpolated($"SELECT * FROM ticket_types WHERE id = {ticketTypeId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (ticketType is null || ticketType.EventSessionId != session.Id)
            {
                throw NotFound();
            }

            TicketInventory? inventory = await db.TicketInventories
                .FromSqlInterpolated($"SELECT * FROM ticket_inventories WHERE ticket_type_id = {ticketTypeId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (inventory is null)
            {
                throw new BookingProblemException(
                    StatusCodes.Status409Conflict,
                    "INSUFFICIENT_CAPACITY",
                    "Ticket inventory is not available.");
            }

            ticketTypes.Add(ticketTypeId, ticketType);
            inventories.Add(ticketTypeId, inventory);
        }

        Guid[] requestedSeatIds = request.Items
            .SelectMany(item => item.SeatIds)
            .Order()
            .ToArray();
        var seats = new Dictionary<Guid, Seat>();
        foreach (Guid seatId in requestedSeatIds)
        {
            Seat? seat = await db.Seats
                .FromSqlInterpolated($"SELECT * FROM seats WHERE id = {seatId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (seat is null)
            {
                throw NotFound();
            }

            seats.Add(seatId, seat);
        }

        DateTimeOffset decisionTime = await DatabaseTimeAsync(db, cancellationToken);
        ValidateSaleState(eventItem, session, request.Items, ticketTypes, decisionTime);
        await ValidateSelectionsAsync(request.Items, ticketTypes, seats, db, cancellationToken);

        decimal totalAmount = 0;
        foreach (CanonicalHoldItem selection in request.Items)
        {
            TicketType ticketType = ticketTypes[selection.TicketTypeId];
            try
            {
                totalAmount = checked(totalAmount + (ticketType.Price * selection.Quantity));
            }
            catch (OverflowException)
            {
                throw Validation("The hold total exceeds the supported monetary range.");
            }
        }

        if (totalAmount > 999999999999.99m)
        {
            throw Validation("The hold total exceeds the supported monetary range.");
        }

        DateTimeOffset expiresAt = decisionTime.AddMinutes(options.HoldDurationMinutes);
        string holdIdempotencyDigest = Sha256($"{user.Id:N}:{CreateHoldScope}:{idempotencyKey}");
        var hold = new Hold(
            user.Id,
            session.Id,
            holdIdempotencyDigest,
            totalAmount,
            decisionTime,
            expiresAt);
        db.Holds.Add(hold);

        foreach (CanonicalHoldItem selection in request.Items)
        {
            TicketType ticketType = ticketTypes[selection.TicketTypeId];
            int affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE ticket_inventories
                SET held_quantity = held_quantity + {selection.Quantity},
                    version = version + 1,
                    updated_at_utc = {decisionTime}
                WHERE ticket_type_id = {selection.TicketTypeId}
                  AND total_quantity - held_quantity - sold_quantity >= {selection.Quantity}
                """, cancellationToken);
            if (affected != 1)
            {
                throw new BookingProblemException(
                    StatusCodes.Status409Conflict,
                    "INSUFFICIENT_CAPACITY",
                    "The requested ticket quantity is no longer available.");
            }

            var holdItem = new HoldItem(
                hold.Id,
                ticketType.Id,
                selection.Quantity,
                ticketType.Price,
                ticketType.MaxPerOrder,
                ticketType.Name);
            db.HoldItems.Add(holdItem);

            foreach (Guid seatId in selection.SeatIds.Order())
            {
                db.SeatAllocations.Add(new SeatAllocation(seatId, holdItem.Id, expiresAt));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return hold.Id;
    }

    private static void ValidateSaleState(
        Event eventItem,
        EventSession session,
        IReadOnlyList<CanonicalHoldItem> selections,
        IReadOnlyDictionary<Guid, TicketType> ticketTypes,
        DateTimeOffset decisionTime)
    {
        if (eventItem.Status != EventStatus.Published
            || session.Status is SessionStatus.Cancelled or SessionStatus.Completed)
        {
            throw new BookingProblemException(
                StatusCodes.Status409Conflict,
                "EVENT_NOT_ON_SALE",
                "The event or session is not available for new holds.");
        }

        foreach (CanonicalHoldItem selection in selections)
        {
            TicketType ticketType = ticketTypes[selection.TicketTypeId];
            DateTimeOffset? effectiveStart = Max(eventItem.SaleStartsAtUtc, ticketType.SaleStartsAtUtc);
            DateTimeOffset effectiveEnd = Min(
                session.StartsAtUtc,
                eventItem.SaleEndsAtUtc,
                ticketType.SaleEndsAtUtc);
            if ((effectiveStart.HasValue && decisionTime < effectiveStart.Value)
                || decisionTime >= effectiveEnd)
            {
                throw new BookingProblemException(
                    StatusCodes.Status409Conflict,
                    "SALES_WINDOW_CLOSED",
                    "The sales window is closed for one or more selected ticket types.");
            }
        }
    }

    private static async Task ValidateSelectionsAsync(
        IReadOnlyList<CanonicalHoldItem> selections,
        IReadOnlyDictionary<Guid, TicketType> ticketTypes,
        IReadOnlyDictionary<Guid, Seat> seats,
        TixFlowDbContext db,
        CancellationToken cancellationToken)
    {
        foreach (CanonicalHoldItem selection in selections)
        {
            TicketType ticketType = ticketTypes[selection.TicketTypeId];
            if (ticketType.InventoryMode != selection.InventoryMode)
            {
                throw new BookingProblemException(
                    StatusCodes.Status422UnprocessableEntity,
                    "INVENTORY_MODE_MISMATCH",
                    "The requested inventory mode does not match the ticket type.");
            }

            if (selection.Quantity > ticketType.MaxPerOrder)
            {
                throw new BookingProblemException(
                    StatusCodes.Status422UnprocessableEntity,
                    "ORDER_LIMIT_EXCEEDED",
                    $"Ticket type {ticketType.Id} allows at most {ticketType.MaxPerOrder} tickets per hold.");
            }

            if (selection.InventoryMode == InventoryMode.GeneralAdmission)
            {
                if (selection.SeatIds.Count != 0)
                {
                    throw Validation("General admission selections cannot contain seat IDs.");
                }

                continue;
            }

            if (selection.SeatIds.Count != selection.Quantity)
            {
                throw Validation("Reserved seating quantity must equal the number of selected seats.");
            }

            foreach (Guid seatId in selection.SeatIds)
            {
                Seat seat = seats[seatId];
                if (!seat.IsActive
                    || seat.EventSessionId != ticketType.EventSessionId
                    || seat.TicketTypeId != ticketType.Id)
                {
                    throw new BookingProblemException(
                        StatusCodes.Status409Conflict,
                        "SEAT_UNAVAILABLE",
                        "One or more selected seats are inactive or do not belong to the ticket type.");
                }
            }
        }

        if (seats.Count > 0)
        {
            Guid[] seatIds = seats.Keys.ToArray();
            bool occupied = await db.SeatAllocations.AnyAsync(
                item => seatIds.Contains(item.SeatId)
                    && (item.Status == SeatAllocationStatus.Held
                        || item.Status == SeatAllocationStatus.Sold),
                cancellationToken);
            if (occupied)
            {
                throw new BookingProblemException(
                    StatusCodes.Status409Conflict,
                    "SEAT_UNAVAILABLE",
                    "One or more selected seats are already held or sold.");
            }
        }
    }

    private static async Task<IResult> GetHoldAsync(
        Guid holdId,
        HttpContext context,
        TixFlowDbContext db,
        CancellationToken cancellationToken)
    {
        User user = CurrentUser(context);
        HoldDto? hold = await LoadAuthorizedHoldAsync(db, holdId, user, cancellationToken);
        if (hold is null)
        {
            return Problem(context, NotFound());
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new HoldEnvelope(hold));
    }

    private static async Task<IResult> ListHoldsAsync(
        HttpContext context,
        TixFlowDbContext db,
        int page = 1,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        User user = CurrentUser(context);
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        IQueryable<Hold> query = db.Holds.AsNoTracking();
        if (user.Role != UserRole.Admin)
        {
            query = query.Where(item => item.UserId == user.Id);
        }

        int total = await query.CountAsync(cancellationToken);
        Guid[] ids = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var data = new List<HoldDto>(ids.Length);
        foreach (Guid id in ids)
        {
            data.Add(await LoadHoldAsync(db, id, cancellationToken));
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            data,
            page = new
            {
                number = page,
                limit,
                total,
                totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)limit)
            }
        });
    }

    private static Task<IResult> CancelHoldAsync(
        Guid holdId,
        CancelHoldRequest request,
        HttpContext context,
        TixFlowDbContext db,
        IOptions<BookingOptions> options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
        {
            return Task.FromResult(Problem(context, Validation("A cancellation reason of 1 to 1000 characters is required.")));
        }

        return CancelHoldCoreAsync(
            holdId, request.Reason.Trim(), context, db, options.Value, cancellationToken);
    }

    private static Task<IResult> DeleteHoldAsync(
        Guid holdId,
        HttpContext context,
        TixFlowDbContext db,
        IOptions<BookingOptions> options,
        CancellationToken cancellationToken)
    {
        string reason = context.Request.Headers["X-Cancellation-Reason"].FirstOrDefault()?.Trim()
            ?? "Cancelled through the Hold API.";
        if (reason.Length is < 1 or > 1000)
        {
            return Task.FromResult(Problem(context, Validation("The cancellation reason must be 1 to 1000 characters.")));
        }

        return CancelHoldCoreAsync(holdId, reason, context, db, options.Value, cancellationToken);
    }

    private static async Task<IResult> CancelHoldCoreAsync(
        Guid holdId,
        string reason,
        HttpContext context,
        TixFlowDbContext db,
        BookingOptions options,
        CancellationToken cancellationToken)
    {
        User user = CurrentUser(context);
        if (!TryReadIdempotencyKey(context, out string idempotencyKey, out IResult? keyError))
        {
            return keyError!;
        }

        if (!TryReadExpectedVersion(context, out long expectedVersion, out IResult? versionError))
        {
            return versionError!;
        }

        bool authorized = await db.Holds.AsNoTracking().AnyAsync(
            item => item.Id == holdId && (user.Role == UserRole.Admin || item.UserId == user.Id),
            cancellationToken);
        if (!authorized)
        {
            return Problem(context, NotFound());
        }

        string scope = $"cancelHold:{holdId:N}";
        string requestHash = Sha256($"{holdId:N}|{reason}|{expectedVersion.ToString(CultureInfo.InvariantCulture)}");
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await ConfigureTransactionAsync(db, options.LockTimeoutSeconds, cancellationToken);
                IdempotencyClaim claim = await ClaimIdempotencyAsync(
                    db,
                    user.Id,
                    scope,
                    idempotencyKey,
                    requestHash,
                    options.IdempotencyRetentionHours,
                    cancellationToken);
                if (!claim.IsNew)
                {
                    IResult replay = ReplayOrConflict(claim.Record, requestHash, context);
                    await transaction.CommitAsync(cancellationToken);
                    return replay;
                }

                await transaction.CreateSavepointAsync("booking_command", cancellationToken);
                try
                {
                    Hold? hold = await db.Holds
                        .FromSqlInterpolated($"SELECT * FROM holds WHERE id = {holdId} FOR UPDATE")
                        .SingleOrDefaultAsync(cancellationToken);
                    if (hold is null || (user.Role != UserRole.Admin && hold.UserId != user.Id))
                    {
                        throw NotFound();
                    }

                    if (hold.Status == HoldStatus.Cancelled)
                    {
                        HoldEnvelope existing = new(await LoadHoldAsync(db, hold.Id, cancellationToken));
                        string existingBody = JsonSerializer.Serialize(existing, JsonOptions);
                        claim.Record.Complete(StatusCodes.Status200OK, existingBody,
                            await DatabaseTimeAsync(db, cancellationToken));
                        await db.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        context.Response.Headers.CacheControl = "no-store";
                        return JsonText(existingBody, StatusCodes.Status200OK);
                    }

                    if (hold.Version != expectedVersion)
                    {
                        throw new BookingProblemException(
                            StatusCodes.Status409Conflict,
                            "VERSION_CONFLICT",
                            "The Hold version does not match X-Expected-Version.");
                    }

                    if (hold.Status != HoldStatus.Active)
                    {
                        throw new BookingProblemException(
                            StatusCodes.Status409Conflict,
                            "INVALID_STATE",
                            $"A Hold in {hold.Status} state cannot be cancelled.");
                    }

                    DateTimeOffset decisionTime = await DatabaseTimeAsync(db, cancellationToken);
                    bool expired = decisionTime >= hold.ExpiresAtUtc;
                    await ReleaseHoldResourcesAsync(db, hold, decisionTime, cancellationToken);
                    if (expired)
                    {
                        hold.Expire(decisionTime);
                    }
                    else
                    {
                        hold.Cancel(decisionTime);
                    }

                    await db.SaveChangesAsync(cancellationToken);
                    if (expired)
                    {
                        throw new PersistedBookingProblemException(
                            StatusCodes.Status409Conflict,
                            "HOLD_EXPIRED",
                            "The Hold reached its deadline and was released.");
                    }

                    HoldEnvelope response = new(await LoadHoldAsync(db, hold.Id, cancellationToken));
                    string responseBody = JsonSerializer.Serialize(response, JsonOptions);
                    claim.Record.Complete(StatusCodes.Status200OK, responseBody, decisionTime);
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.ReleaseSavepointAsync("booking_command", cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    context.Response.Headers.CacheControl = "no-store";
                    return JsonText(responseBody, StatusCodes.Status200OK);
                }
                catch (PersistedBookingProblemException problem)
                {
                    string body = SerializeProblem(context, problem);
                    claim.Record.Complete(problem.Status, body, await DatabaseTimeAsync(db, cancellationToken));
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.ReleaseSavepointAsync("booking_command", cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return ProblemText(body, problem.Status);
                }
                catch (BookingProblemException problem)
                {
                    return await CommitBusinessProblemAsync(
                        db, transaction, claim.Record.Id, problem, context, cancellationToken);
                }
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            context.Response.Headers.RetryAfter = "1";
            return Problem(context, new BookingProblemException(
                StatusCodes.Status409Conflict,
                "REQUEST_IN_PROGRESS",
                "A conflicting booking request is still in progress.",
                retryable: true));
        }
    }

    private static async Task ReleaseHoldResourcesAsync(
        TixFlowDbContext db,
        Hold hold,
        DateTimeOffset decisionTime,
        CancellationToken cancellationToken)
    {
        HoldItem[] items = await db.HoldItems
            .Where(item => item.HoldId == hold.Id)
            .OrderBy(item => item.TicketTypeId)
            .ToArrayAsync(cancellationToken);

        foreach (HoldItem item in items)
        {
            _ = await db.TicketInventories
                .FromSqlInterpolated($"SELECT * FROM ticket_inventories WHERE ticket_type_id = {item.TicketTypeId} FOR UPDATE")
                .SingleAsync(cancellationToken);
        }

        foreach (HoldItem item in items)
        {
            int affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE ticket_inventories
                SET held_quantity = held_quantity - {item.Quantity},
                    version = version + 1,
                    updated_at_utc = {decisionTime}
                WHERE ticket_type_id = {item.TicketTypeId}
                  AND held_quantity >= {item.Quantity}
                """, cancellationToken);
            if (affected != 1)
            {
                throw new InvalidOperationException(
                    $"Inventory projection is inconsistent for ticket type {item.TicketTypeId}.");
            }
        }

        Guid[] itemIds = items.Select(item => item.Id).ToArray();
        if (itemIds.Length > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE seat_allocations
                SET status = 'Released', updated_at_utc = {decisionTime}
                WHERE hold_item_id = ANY ({itemIds}) AND status = 'Held'
                """, cancellationToken);
        }
    }

    private static async Task<IdempotencyClaim> ClaimIdempotencyAsync(
        TixFlowDbContext db,
        Guid userId,
        string scope,
        string key,
        string requestHash,
        int retentionHours,
        CancellationToken cancellationToken)
    {
        Guid recordId = Guid.NewGuid();
        DateTimeOffset now = await DatabaseTimeAsync(db, cancellationToken);
        DateTimeOffset expiresAt = now.AddHours(retentionHours);
        int inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO idempotency_records
                (id, user_id, scope, key, request_hash, is_completed,
                 expires_at_utc, created_at_utc, updated_at_utc)
            VALUES
                ({recordId}, {userId}, {scope}, {key}, {requestHash}, false,
                 {expiresAt}, {now}, {now})
            ON CONFLICT (user_id, scope, key) DO NOTHING
            """, cancellationToken);

        IdempotencyRecord record = await db.IdempotencyRecords
            .FromSqlInterpolated($"""
                SELECT * FROM idempotency_records
                WHERE user_id = {userId} AND scope = {scope} AND key = {key}
                FOR UPDATE
                """)
            .SingleAsync(cancellationToken);
        return new IdempotencyClaim(record, inserted == 1);
    }

    private static IResult ReplayOrConflict(
        IdempotencyRecord record,
        string requestHash,
        HttpContext context)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(record.RequestHash),
                Encoding.ASCII.GetBytes(requestHash)))
        {
            return Problem(context, new BookingProblemException(
                StatusCodes.Status409Conflict,
                "IDEMPOTENCY_KEY_REUSED",
                "The Idempotency-Key was already used with a different request."));
        }

        if (!record.IsCompleted || record.ResponseStatusCode is null || record.ResponseBody is null)
        {
            context.Response.Headers.RetryAfter = "1";
            return Problem(context, new BookingProblemException(
                StatusCodes.Status409Conflict,
                "REQUEST_IN_PROGRESS",
                "The original request is still in progress.",
                retryable: true));
        }

        if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return Problem(context, new BookingProblemException(
                StatusCodes.Status409Conflict,
                "IDEMPOTENCY_RESULT_EXPIRED",
                "The cached idempotency result has expired; do not reuse this key."));
        }

        context.Response.Headers["Idempotency-Replayed"] = "true";
        context.Response.Headers.CacheControl = "no-store";
        return record.ResponseStatusCode >= 400
            ? ProblemText(record.ResponseBody, record.ResponseStatusCode.Value)
            : JsonText(record.ResponseBody, record.ResponseStatusCode.Value);
    }

    private static async Task<IResult> CommitBusinessProblemAsync(
        TixFlowDbContext db,
        IDbContextTransaction transaction,
        Guid recordId,
        BookingProblemException problem,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackToSavepointAsync("booking_command", cancellationToken);
        db.ChangeTracker.Clear();
        IdempotencyRecord record = await db.IdempotencyRecords
            .FromSqlInterpolated($"SELECT * FROM idempotency_records WHERE id = {recordId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        string body = SerializeProblem(context, problem);
        record.Complete(problem.Status, body, await DatabaseTimeAsync(db, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.ReleaseSavepointAsync("booking_command", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ProblemText(body, problem.Status);
    }

    private static async Task ConfigureTransactionAsync(
        TixFlowDbContext db,
        int lockTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        string lockTimeout = $"{lockTimeoutSeconds * 1000}ms";
        string statementTimeout = $"{Math.Max(15, lockTimeoutSeconds * 3) * 1000}ms";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('lock_timeout', {lockTimeout}, true)", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('statement_timeout', {statementTimeout}, true)", cancellationToken);
    }

    private static async Task<DateTimeOffset> DatabaseTimeAsync(
        TixFlowDbContext db,
        CancellationToken cancellationToken)
    {
        await using System.Data.Common.DbCommand command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT clock_timestamp()";
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        if (command.Connection?.State != System.Data.ConnectionState.Open)
        {
            await command.Connection!.OpenAsync(cancellationToken);
        }

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("PostgreSQL did not return its current time.")
        };
    }

    private static async Task<HoldDto?> LoadAuthorizedHoldAsync(
        TixFlowDbContext db,
        Guid holdId,
        User user,
        CancellationToken cancellationToken)
    {
        bool authorized = await db.Holds.AsNoTracking().AnyAsync(
            item => item.Id == holdId && (user.Role == UserRole.Admin || item.UserId == user.Id),
            cancellationToken);
        return authorized ? await LoadHoldAsync(db, holdId, cancellationToken) : null;
    }

    private static async Task<HoldDto> LoadHoldAsync(
        TixFlowDbContext db,
        Guid holdId,
        CancellationToken cancellationToken)
    {
        Hold hold = await db.Holds
            .AsNoTracking()
            .Include(item => item.Items)
                .ThenInclude(item => item.TicketType)
            .Include(item => item.Items)
                .ThenInclude(item => item.SeatAllocations)
            .SingleAsync(item => item.Id == holdId, cancellationToken);
        Guid? orderId = await db.Orders.AsNoTracking()
            .Where(item => item.HoldId == holdId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);

        HoldItemDto[] items = hold.Items
            .OrderBy(item => item.TicketTypeId)
            .Select(item => new HoldItemDto(
                item.Id,
                item.TicketTypeId,
                item.TicketType.InventoryMode,
                item.Quantity,
                item.UnitPrice,
                item.UnitPrice * item.Quantity,
                item.SeatAllocations
                    .Where(allocation => allocation.Status != SeatAllocationStatus.Released)
                    .OrderBy(allocation => allocation.SeatId)
                    .Select(allocation => allocation.SeatId)
                    .ToArray(),
                item.MaxPerOrderSnapshot))
            .ToArray();

        return new HoldDto(
            hold.Id,
            hold.EventSessionId,
            hold.Status,
            hold.Currency,
            hold.TotalAmount,
            hold.ExpiresAtUtc,
            hold.CreatedAtUtc,
            hold.UpdatedAtUtc,
            orderId,
            items,
            hold.Version);
    }

    private static CanonicalHoldRequest Canonicalize(CreateHoldRequest request)
    {
        if (request.EventSessionId == Guid.Empty)
        {
            throw Validation("eventSessionId is required.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw Validation("At least one hold item is required.");
        }

        var grouped = new Dictionary<Guid, MutableCanonicalItem>();
        var allSeatIds = new HashSet<Guid>();
        foreach (HoldSelectionRequest item in request.Items)
        {
            if (item.TicketTypeId == Guid.Empty || item.Quantity <= 0)
            {
                throw Validation("Every hold item needs a ticketTypeId and a positive quantity.");
            }

            Guid[] seatIds = item.SeatIds?.ToArray() ?? [];
            if (seatIds.Any(id => id == Guid.Empty))
            {
                throw Validation("seatIds cannot contain an empty UUID.");
            }

            foreach (Guid seatId in seatIds)
            {
                if (!allSeatIds.Add(seatId))
                {
                    throw new BookingProblemException(
                        StatusCodes.Status422UnprocessableEntity,
                        "DUPLICATE_SEAT",
                        "A seat ID cannot occur more than once in a Hold request.");
                }
            }

            if (!grouped.TryGetValue(item.TicketTypeId, out MutableCanonicalItem? existing))
            {
                grouped.Add(item.TicketTypeId, new MutableCanonicalItem(
                    item.TicketTypeId, item.InventoryMode, item.Quantity, seatIds.ToList()));
                continue;
            }

            if (existing.InventoryMode != item.InventoryMode)
            {
                throw new BookingProblemException(
                    StatusCodes.Status422UnprocessableEntity,
                    "INVENTORY_MODE_MISMATCH",
                    "The same ticket type cannot use different inventory modes.");
            }

            try
            {
                existing.Quantity = checked(existing.Quantity + item.Quantity);
            }
            catch (OverflowException)
            {
                throw Validation("The requested quantity is too large.");
            }
            existing.SeatIds.AddRange(seatIds);
        }

        CanonicalHoldItem[] items = grouped.Values
            .OrderBy(item => item.TicketTypeId)
            .Select(item => new CanonicalHoldItem(
                item.TicketTypeId,
                item.InventoryMode,
                item.Quantity,
                item.SeatIds.Order().ToArray()))
            .ToArray();
        return new CanonicalHoldRequest(request.EventSessionId, items);
    }

    private static string HashCanonicalRequest(CanonicalHoldRequest request)
    {
        var canonical = new
        {
            eventSessionId = request.EventSessionId.ToString("D"),
            items = request.Items.Select(item => new
            {
                ticketTypeId = item.TicketTypeId.ToString("D"),
                inventoryMode = item.InventoryMode.ToString(),
                item.Quantity,
                seatIds = item.SeatIds.Select(id => id.ToString("D"))
            })
        };
        return Sha256(JsonSerializer.Serialize(canonical, JsonOptions));
    }

    private static bool TryReadIdempotencyKey(
        HttpContext context,
        out string key,
        out IResult? error)
    {
        key = context.Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty;
        if (key.Length is < 1 or > 120 || key.Any(character => character is < '!' or > '~'))
        {
            error = Problem(context, new BookingProblemException(
                StatusCodes.Status400BadRequest,
                "IDEMPOTENCY_KEY_REQUIRED",
                "Idempotency-Key must contain 1 to 120 visible ASCII characters without whitespace."));
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadExpectedVersion(
        HttpContext context,
        out long version,
        out IResult? error)
    {
        string value = context.Request.Headers["X-Expected-Version"].FirstOrDefault() ?? string.Empty;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version) || version < 0)
        {
            error = Problem(context, new BookingProblemException(
                StatusCodes.Status400BadRequest,
                "EXPECTED_VERSION_REQUIRED",
                "X-Expected-Version must be a non-negative integer."));
            return false;
        }

        error = null;
        return true;
    }

    private static User CurrentUser(HttpContext context) =>
        context.Items[LocalUserMiddleware.UserKey] as User
        ?? throw new InvalidOperationException("The authorized local user is unavailable.");

    private static IResult Problem(HttpContext context, BookingProblemException problem) =>
        ProblemText(SerializeProblem(context, problem), problem.Status);

    private static string SerializeProblem(HttpContext context, BookingProblemException problem) =>
        JsonSerializer.Serialize(new ProblemPayload(
            $"/problems/{problem.Code.ToLowerInvariant().Replace('_', '-')}",
            problem.Code,
            problem.Status,
            problem.Detail,
            context.Request.Path,
            problem.Code,
            context.TraceIdentifier,
            problem.Retryable), JsonOptions);

    private static IResult JsonText(string body, int status) =>
        Results.Text(body, "application/json", Encoding.UTF8, status);

    private static IResult ProblemText(string body, int status) =>
        Results.Text(body, "application/problem+json", Encoding.UTF8, status);

    private static BookingProblemException Validation(string detail) => new(
        StatusCodes.Status422UnprocessableEntity,
        "VALIDATION_ERROR",
        detail);

    private static BookingProblemException NotFound() => new(
        StatusCodes.Status404NotFound,
        "RESOURCE_NOT_FOUND",
        "The requested resource was not found.");

    private static bool IsActiveSeatConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && postgres.ConstraintName == "ux_seat_allocations_active_seat";

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return left.Value >= right.Value ? left : right;
    }

    private static DateTimeOffset Min(
        DateTimeOffset required,
        DateTimeOffset? first,
        DateTimeOffset? second)
    {
        DateTimeOffset result = required;
        if (first.HasValue && first.Value < result) result = first.Value;
        if (second.HasValue && second.Value < result) result = second.Value;
        return result;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public sealed record CreateHoldRequest(Guid EventSessionId, IReadOnlyList<HoldSelectionRequest> Items);

    public sealed record HoldSelectionRequest(
        Guid TicketTypeId,
        InventoryMode InventoryMode,
        int Quantity,
        IReadOnlyList<Guid>? SeatIds);

    public sealed record CancelHoldRequest(string Reason);

    private sealed record CanonicalHoldRequest(Guid EventSessionId, IReadOnlyList<CanonicalHoldItem> Items);

    private sealed record CanonicalHoldItem(
        Guid TicketTypeId,
        InventoryMode InventoryMode,
        int Quantity,
        IReadOnlyList<Guid> SeatIds);

    private sealed class MutableCanonicalItem(
        Guid ticketTypeId,
        InventoryMode inventoryMode,
        int quantity,
        List<Guid> seatIds)
    {
        public Guid TicketTypeId { get; } = ticketTypeId;
        public InventoryMode InventoryMode { get; } = inventoryMode;
        public int Quantity { get; set; } = quantity;
        public List<Guid> SeatIds { get; } = seatIds;
    }

    private sealed record SessionLockReference(Guid Id, Guid EventId);

    private sealed record IdempotencyClaim(IdempotencyRecord Record, bool IsNew);

    private sealed record HoldEnvelope(HoldDto Data);

    private sealed record HoldDto(
        Guid Id,
        Guid EventSessionId,
        HoldStatus Status,
        string Currency,
        decimal TotalAmount,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        Guid? OrderId,
        IReadOnlyList<HoldItemDto> Items,
        long Version);

    private sealed record HoldItemDto(
        Guid Id,
        Guid TicketTypeId,
        InventoryMode InventoryMode,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal,
        IReadOnlyList<Guid> SeatIds,
        int MaxPerOrderSnapshot);

    private sealed record ProblemPayload(
        string Type,
        string Title,
        int Status,
        string Detail,
        string Instance,
        string Code,
        string TraceId,
        bool Retryable);

    private class BookingProblemException(
        int status,
        string code,
        string detail,
        bool retryable = false) : Exception(detail)
    {
        public int Status { get; } = status;
        public string Code { get; } = code;
        public string Detail { get; } = detail;
        public bool Retryable { get; } = retryable;
    }

    private sealed class PersistedBookingProblemException(
        int status,
        string code,
        string detail) : BookingProblemException(status, code, detail);
}
