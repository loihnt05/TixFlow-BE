using Microsoft.EntityFrameworkCore;
using Npgsql;
using TixFlow.Api.Authentication;
using TixFlow.Domain.Booking;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.Events;

public static class InventoryModule
{
    public static IEndpointRouteBuilder MapInventoryModule(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder management = endpoints.MapGroup("/management")
            .WithTags("Inventory Management")
            .RequireAuthorization("OrganizerOrAdmin");

        management.MapGet("/event-sessions/{sessionId:guid}/ticket-types", GetManagedTicketTypesAsync);
        management.MapPost("/event-sessions/{sessionId:guid}/ticket-types", CreateTicketTypeAsync);
        management.MapGet("/ticket-types/{ticketTypeId:guid}", GetManagedTicketTypeAsync);
        management.MapPatch("/ticket-types/{ticketTypeId:guid}", UpdateTicketTypeAsync);
        management.MapDelete("/ticket-types/{ticketTypeId:guid}", DeleteTicketTypeAsync);
        management.MapPut("/ticket-types/{ticketTypeId:guid}/capacity", UpdateCapacityAsync);
        management.MapGet("/ticket-types/{ticketTypeId:guid}/seat-map", GetSeatMapAsync);
        management.MapPut("/ticket-types/{ticketTypeId:guid}/seat-map", ReplaceSeatMapAsync);
        management.MapPost("/ticket-types/{ticketTypeId:guid}/seats", CreateSeatAsync);
        management.MapGet("/seats/{seatId:guid}", GetSeatAsync);
        management.MapPut("/seats/{seatId:guid}", UpdateSeatAsync);
        management.MapDelete("/seats/{seatId:guid}", DeleteSeatAsync);

        RouteGroupBuilder publicApi = endpoints.MapGroup(string.Empty).WithTags("Availability");
        publicApi.MapGet("/event-sessions/{sessionId:guid}/ticket-types", GetPublicTicketTypesAsync);
        publicApi.MapGet("/event-sessions/{sessionId:guid}/availability", GetSessionAvailabilityAsync);
        publicApi.MapGet("/event-sessions/{sessionId:guid}/seats", GetPublicSeatsAsync);
        publicApi.MapGet("/ticket-types/{ticketTypeId:guid}/availability", GetTicketAvailabilityAsync);
        return endpoints;
    }

    private static async Task<IResult> GetManagedTicketTypesAsync(Guid sessionId, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await ScopedSessions(dbContext.EventSessions, context)
                .AnyAsync(item => item.Id == sessionId, cancellationToken))
            return NotFound("SESSION_NOT_FOUND", "Session was not found.");

        TicketType[] items = await ManagedTicketTypes(dbContext)
            .Where(item => item.EventSessionId == sessionId)
            .OrderBy(item => item.Price).ThenBy(item => item.Code)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(items.Select(ToManagedResponse).ToArray());
    }

    private static async Task<IResult> GetManagedTicketTypeAsync(Guid ticketTypeId, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        TicketType? item = await ScopedTicketTypes(ManagedTicketTypes(dbContext), context)
            .SingleOrDefaultAsync(candidate => candidate.Id == ticketTypeId, cancellationToken);
        return item is null ? NotFound("TICKET_TYPE_NOT_FOUND", "Ticket type was not found.")
            : Results.Ok(ToManagedResponse(item));
    }

    private static async Task<IResult> CreateTicketTypeAsync(Guid sessionId, TicketTypeRequest request,
        HttpContext context, TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        EventSession? session = await ScopedSessions(dbContext.EventSessions.Include(item => item.Event), context)
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        if (session is null) return NotFound("SESSION_NOT_FOUND", "Session was not found.");
        if (session.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Inventory can only be configured while the event is a draft.");

        SeatRequest[] seats = request.Seats ?? [];
        IResult? validation = ValidateTicketTypeRequest(request, seats);
        if (validation is not null) return validation;

        try
        {
            var ticketType = new TicketType(sessionId, request.Name, request.Code, request.Price,
                request.InventoryMode, request.Capacity);
            ticketType.Update(request.Name, request.Code, request.Description, request.Price,
                request.MaxPerOrder, request.SaleStartsAtUtc, request.SaleEndsAtUtc);
            dbContext.TicketTypes.Add(ticketType);
            dbContext.TicketInventories.Add(new TicketInventory(ticketType.Id, request.Capacity));
            foreach (SeatRequest seat in seats)
                dbContext.Seats.Add(new Seat(sessionId, ticketType.Id, seat.Section, seat.RowLabel,
                    seat.SeatNumber, seat.IsAccessible));
            await dbContext.SaveChangesAsync(cancellationToken);
            TicketType created = await ManagedTicketTypes(dbContext)
                .SingleAsync(item => item.Id == ticketType.Id, cancellationToken);
            return Results.Created($"/api/v1/management/ticket-types/{ticketType.Id}", ToManagedResponse(created));
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_TICKET_TYPE", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("TICKET_TYPE_CONFLICT", "Ticket code or seat code is already used in this session.");
        }
    }

    private static async Task<IResult> UpdateTicketTypeAsync(Guid ticketTypeId, TicketTypeUpdateRequest request,
        HttpContext context, TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        TicketType? item = await ScopedTicketTypes(dbContext.TicketTypes
                .Include(ticket => ticket.EventSession).ThenInclude(session => session.Event)
                .Include(ticket => ticket.Inventory).Include(ticket => ticket.Seats), context)
            .SingleOrDefaultAsync(ticket => ticket.Id == ticketTypeId, cancellationToken);
        if (item is null) return NotFound("TICKET_TYPE_NOT_FOUND", "Ticket type was not found.");
        if (item.EventSession.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Ticket types can only be edited while the event is a draft.");
        if (item.Version != request.Version) return VersionConflict(item.Version);

        try
        {
            item.Update(request.Name, request.Code, request.Description, request.Price, request.MaxPerOrder,
                request.SaleStartsAtUtc, request.SaleEndsAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToManagedResponse(item));
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_TICKET_TYPE", exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("VERSION_CONFLICT", "The ticket type was changed by another request.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("TICKET_TYPE_CODE_CONFLICT", "This session already uses the ticket code.");
        }
    }

    private static async Task<IResult> UpdateCapacityAsync(Guid ticketTypeId, CapacityRequest request,
        HttpContext context, TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        TicketType? item = await ScopedTicketTypes(dbContext.TicketTypes
                .Include(ticket => ticket.EventSession).ThenInclude(session => session.Event)
                .Include(ticket => ticket.Inventory), context)
            .SingleOrDefaultAsync(ticket => ticket.Id == ticketTypeId, cancellationToken);
        if (item is null) return NotFound("TICKET_TYPE_NOT_FOUND", "Ticket type was not found.");
        if (item.EventSession.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Capacity can only be edited while the event is a draft.");
        if (item.Version != request.Version) return VersionConflict(item.Version);
        if (item.InventoryMode == InventoryMode.ReservedSeating)
            return Conflict("SEAT_MAP_REQUIRED", "Reserved seating capacity is changed through the seat map.");

        try
        {
            item.SetCapacity(request.Capacity);
            item.Inventory.SetTotalQuantity(request.Capacity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToManagedResponse(item));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict("CAPACITY_BELOW_COMMITTED", exception.Message);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest("INVALID_CAPACITY", exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("VERSION_CONFLICT", "The inventory was changed by another request.");
        }
    }

    private static async Task<IResult> DeleteTicketTypeAsync(Guid ticketTypeId, long version,
        HttpContext context, TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        TicketType? item = await ScopedTicketTypes(dbContext.TicketTypes
                .Include(ticket => ticket.EventSession).ThenInclude(session => session.Event), context)
            .SingleOrDefaultAsync(ticket => ticket.Id == ticketTypeId, cancellationToken);
        if (item is null) return NotFound("TICKET_TYPE_NOT_FOUND", "Ticket type was not found.");
        if (item.EventSession.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Ticket types can only be deleted while the event is a draft.");
        if (item.Version != version) return VersionConflict(item.Version);
        bool hasTransactions = await dbContext.HoldItems.AnyAsync(row => row.TicketTypeId == ticketTypeId, cancellationToken)
            || await dbContext.OrderItems.AnyAsync(row => row.TicketTypeId == ticketTypeId, cancellationToken);
        if (hasTransactions)
            return Conflict("TICKET_TYPE_IN_USE", "A ticket type with booking transactions cannot be deleted.");
        dbContext.TicketTypes.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetSeatMapAsync(Guid ticketTypeId, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        TicketType? item = await ScopedTicketTypes(ManagedTicketTypes(dbContext), context)
            .SingleOrDefaultAsync(candidate => candidate.Id == ticketTypeId, cancellationToken);
        if (item is null) return NotFound("TICKET_TYPE_NOT_FOUND", "Ticket type was not found.");
        return Results.Ok(ToSeatMapResponse(item, includeInactive: true));
    }

    private static async Task<IResult> ReplaceSeatMapAsync(Guid ticketTypeId, SeatMapRequest request,
        HttpContext context, TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        TicketType? item = await ScopedTicketTypes(dbContext.TicketTypes
                .Include(ticket => ticket.EventSession).ThenInclude(session => session.Event)
                .Include(ticket => ticket.Inventory)
                .Include(ticket => ticket.Seats).ThenInclude(seat => seat.Allocations), context)
            .SingleOrDefaultAsync(ticket => ticket.Id == ticketTypeId, cancellationToken);
        if (item is null) return NotFound("TICKET_TYPE_NOT_FOUND", "Ticket type was not found.");
        if (item.EventSession.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Seat maps can only be edited while the event is a draft.");
        if (item.InventoryMode != InventoryMode.ReservedSeating)
            return Conflict("INVALID_INVENTORY_MODE", "Only reserved seating ticket types have a seat map.");
        if (item.Version != request.Version) return VersionConflict(item.Version);
        IResult? validation = ValidateSeatMap(request.Seats);
        if (validation is not null) return validation;

        Dictionary<Guid, Seat> existingById = item.Seats.ToDictionary(seat => seat.Id);
        HashSet<Guid> retainedIds = request.Seats.Where(seat => seat.Id.HasValue)
            .Select(seat => seat.Id!.Value).ToHashSet();
        if (retainedIds.Any(id => !existingById.ContainsKey(id)))
            return BadRequest("INVALID_SEAT", "The seat map contains a seat that does not belong to this ticket type.");

        Guid[] removableIds = item.Seats.Where(seat => !retainedIds.Contains(seat.Id)).Select(seat => seat.Id).ToArray();
        if (removableIds.Length > 0 && (await dbContext.SeatAllocations.AnyAsync(
                allocation => removableIds.Contains(allocation.SeatId), cancellationToken)
            || await dbContext.Tickets.AnyAsync(ticket => ticket.SeatId.HasValue
                && removableIds.Contains(ticket.SeatId.Value), cancellationToken)))
            return Conflict("SEAT_IN_USE", "Seats with booking history cannot be removed from the seat map.");

        foreach (SeatRequest requested in request.Seats)
        {
            if (requested.Id.HasValue)
            {
                Seat seat = existingById[requested.Id.Value];
                if (!requested.IsActive && seat.Allocations.Any(IsActiveAllocation))
                    return Conflict("SEAT_IN_USE", "A held or sold seat cannot be disabled.");
                seat.Update(requested.Section, requested.RowLabel, requested.SeatNumber,
                    requested.IsAccessible, requested.IsActive);
            }
            else
            {
                var seat = new Seat(item.EventSessionId, item.Id, requested.Section, requested.RowLabel,
                    requested.SeatNumber, requested.IsAccessible);
                if (!requested.IsActive)
                    seat.Update(requested.Section, requested.RowLabel, requested.SeatNumber,
                        requested.IsAccessible, false);
                dbContext.Seats.Add(seat);
            }
        }
        if (removableIds.Length > 0)
            dbContext.Seats.RemoveRange(item.Seats.Where(seat => removableIds.Contains(seat.Id)));

        int activeCount = request.Seats.Count(seat => seat.IsActive);
        if (activeCount <= 0) return BadRequest("EMPTY_SEAT_MAP", "A reserved ticket type needs at least one active seat.");
        try
        {
            item.SetCapacity(activeCount);
            item.Inventory.SetTotalQuantity(activeCount);
            await dbContext.SaveChangesAsync(cancellationToken);
            TicketType updated = await ManagedTicketTypes(dbContext)
                .SingleAsync(ticket => ticket.Id == item.Id, cancellationToken);
            return Results.Ok(ToSeatMapResponse(updated, includeInactive: true));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict("CAPACITY_BELOW_COMMITTED", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("SEAT_CODE_CONFLICT", "Seat codes must be unique within a session.");
        }
    }

    private static async Task<IResult> CreateSeatAsync(Guid ticketTypeId, SeatRequest request,
        HttpContext context, TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        TicketType? item = await ScopedTicketTypes(dbContext.TicketTypes
                .Include(ticket => ticket.EventSession).ThenInclude(session => session.Event)
                .Include(ticket => ticket.Inventory), context)
            .SingleOrDefaultAsync(ticket => ticket.Id == ticketTypeId, cancellationToken);
        if (item is null) return NotFound("TICKET_TYPE_NOT_FOUND", "Ticket type was not found.");
        if (item.EventSession.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Seats can only be edited while the event is a draft.");
        if (item.InventoryMode != InventoryMode.ReservedSeating)
            return Conflict("INVALID_INVENTORY_MODE", "Only reserved seating ticket types have seats.");
        if (!request.IsActive) return BadRequest("INVALID_SEAT", "New seats must be active.");
        try
        {
            var seat = new Seat(item.EventSessionId, item.Id, request.Section, request.RowLabel,
                request.SeatNumber, request.IsAccessible);
            item.SetCapacity(item.Capacity + 1);
            item.Inventory.SetTotalQuantity(item.Capacity);
            dbContext.Seats.Add(seat);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/management/seats/{seat.Id}", ToSeatResponse(seat));
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_SEAT", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("SEAT_CODE_CONFLICT", "Seat codes must be unique within a session.");
        }
    }

    private static async Task<IResult> GetSeatAsync(Guid seatId, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        Seat? seat = await ScopedSeats(dbContext.Seats.AsNoTracking().Include(item => item.Allocations), context)
            .SingleOrDefaultAsync(item => item.Id == seatId, cancellationToken);
        return seat is null ? NotFound("SEAT_NOT_FOUND", "Seat was not found.")
            : Results.Ok(ToSeatResponse(seat));
    }

    private static async Task<IResult> UpdateSeatAsync(Guid seatId, SeatRequest request, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        Seat? seat = await ScopedSeats(dbContext.Seats
                .Include(item => item.EventSession).ThenInclude(session => session.Event)
                .Include(item => item.TicketType).ThenInclude(ticket => ticket.Inventory)
                .Include(item => item.Allocations), context)
            .SingleOrDefaultAsync(item => item.Id == seatId, cancellationToken);
        if (seat is null) return NotFound("SEAT_NOT_FOUND", "Seat was not found.");
        if (seat.EventSession.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Seats can only be edited while the event is a draft.");
        if (!request.IsActive && seat.Allocations.Any(IsActiveAllocation))
            return Conflict("SEAT_IN_USE", "A held or sold seat cannot be disabled.");

        bool activeChanged = seat.IsActive != request.IsActive;
        int capacity = seat.TicketType.Capacity + (request.IsActive ? 1 : -1);
        if (activeChanged && capacity <= 0)
            return BadRequest("EMPTY_SEAT_MAP", "A reserved ticket type needs at least one active seat.");
        try
        {
            seat.Update(request.Section, request.RowLabel, request.SeatNumber,
                request.IsAccessible, request.IsActive);
            if (activeChanged)
            {
                seat.TicketType.SetCapacity(capacity);
                seat.TicketType.Inventory.SetTotalQuantity(capacity);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToSeatResponse(seat));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict("CAPACITY_BELOW_COMMITTED", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_SEAT", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("SEAT_CODE_CONFLICT", "Seat codes must be unique within a session.");
        }
    }

    private static async Task<IResult> DeleteSeatAsync(Guid seatId, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        Seat? seat = await ScopedSeats(dbContext.Seats
                .Include(item => item.EventSession).ThenInclude(session => session.Event)
                .Include(item => item.TicketType).ThenInclude(ticket => ticket.Inventory), context)
            .SingleOrDefaultAsync(item => item.Id == seatId, cancellationToken);
        if (seat is null) return NotFound("SEAT_NOT_FOUND", "Seat was not found.");
        if (seat.EventSession.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Seats can only be deleted while the event is a draft.");
        if (await dbContext.SeatAllocations.AnyAsync(item => item.SeatId == seatId, cancellationToken)
            || await dbContext.Tickets.AnyAsync(item => item.SeatId == seatId, cancellationToken))
            return Conflict("SEAT_IN_USE", "A seat with booking history cannot be deleted.");
        int capacity = seat.TicketType.Capacity - (seat.IsActive ? 1 : 0);
        if (capacity <= 0) return BadRequest("EMPTY_SEAT_MAP", "A reserved ticket type needs at least one active seat.");
        seat.TicketType.SetCapacity(capacity);
        seat.TicketType.Inventory.SetTotalQuantity(capacity);
        dbContext.Seats.Remove(seat);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetPublicTicketTypesAsync(Guid sessionId,
        TixFlowDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        EventSession? session = await PublicSessions(dbContext)
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        return session is null ? NotFound("SESSION_NOT_FOUND", "Published session was not found.")
            : Results.Ok(session.TicketTypes.OrderBy(item => item.Price)
                .Select(item => ToAvailabilityResponse(item, timeProvider.GetUtcNow())).ToArray());
    }

    private static async Task<IResult> GetSessionAvailabilityAsync(Guid sessionId,
        TixFlowDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        EventSession? session = await PublicSessions(dbContext)
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        if (session is null) return NotFound("SESSION_NOT_FOUND", "Published session was not found.");
        DateTimeOffset snapshotAt = timeProvider.GetUtcNow();
        return Results.Ok(new SessionAvailabilityResponse(session.Id, session.Version, snapshotAt,
            session.TicketTypes.OrderBy(item => item.Price)
                .Select(item => ToAvailabilityResponse(item, snapshotAt)).ToArray()));
    }

    private static async Task<IResult> GetTicketAvailabilityAsync(Guid ticketTypeId,
        TixFlowDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        TicketType? item = await PublicTicketTypes(dbContext)
            .SingleOrDefaultAsync(ticket => ticket.Id == ticketTypeId, cancellationToken);
        return item is null ? NotFound("TICKET_TYPE_NOT_FOUND", "Published ticket type was not found.")
            : Results.Ok(ToAvailabilityResponse(item, timeProvider.GetUtcNow()));
    }

    private static async Task<IResult> GetPublicSeatsAsync(Guid sessionId, Guid? ticketTypeId,
        TixFlowDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!await dbContext.EventSessions.AsNoTracking().AnyAsync(item => item.Id == sessionId
                && item.Event.Status == EventStatus.Published
                && item.Status != SessionStatus.Cancelled && item.Status != SessionStatus.Completed,
                cancellationToken))
            return NotFound("SESSION_NOT_FOUND", "Published session was not found.");
        Seat[] seats = await dbContext.Seats.AsNoTracking().Include(item => item.Allocations)
            .Where(item => item.EventSessionId == sessionId && item.IsActive
                && (!ticketTypeId.HasValue || item.TicketTypeId == ticketTypeId))
            .OrderBy(item => item.Section).ThenBy(item => item.RowLabel).ThenBy(item => item.SeatNumber)
            .ToArrayAsync(cancellationToken);
        DateTimeOffset snapshotAt = timeProvider.GetUtcNow();
        return Results.Ok(new SeatAvailabilityResponse(sessionId, ticketTypeId, snapshotAt,
            seats.Select(ToPublicSeatResponse).ToArray()));
    }

    private static IQueryable<TicketType> ManagedTicketTypes(TixFlowDbContext dbContext) =>
        dbContext.TicketTypes.AsNoTracking().Include(item => item.Inventory)
            .Include(item => item.Seats).ThenInclude(seat => seat.Allocations);

    private static IQueryable<EventSession> PublicSessions(TixFlowDbContext dbContext) =>
        dbContext.EventSessions.AsNoTracking()
            .Include(item => item.TicketTypes).ThenInclude(item => item.Inventory)
            .Include(item => item.TicketTypes).ThenInclude(item => item.Seats).ThenInclude(item => item.Allocations)
            .Where(item => item.Event.Status == EventStatus.Published
                && item.Status != SessionStatus.Cancelled && item.Status != SessionStatus.Completed)
            .AsSplitQuery();

    private static IQueryable<TicketType> PublicTicketTypes(TixFlowDbContext dbContext) =>
        dbContext.TicketTypes.AsNoTracking().Include(item => item.Inventory)
            .Include(item => item.Seats).ThenInclude(seat => seat.Allocations)
            .Where(item => item.EventSession.Event.Status == EventStatus.Published
                && item.EventSession.Status != SessionStatus.Cancelled
                && item.EventSession.Status != SessionStatus.Completed)
            .AsSplitQuery();

    private static IQueryable<EventSession> ScopedSessions(IQueryable<EventSession> query, HttpContext context) =>
        IsAdmin(context) ? query : query.Where(item => item.Event.Organizer.OwnerUserId == CurrentUser(context).Id);

    private static IQueryable<TicketType> ScopedTicketTypes(IQueryable<TicketType> query, HttpContext context) =>
        IsAdmin(context) ? query : query.Where(item => item.EventSession.Event.Organizer.OwnerUserId == CurrentUser(context).Id);

    private static IQueryable<Seat> ScopedSeats(IQueryable<Seat> query, HttpContext context) =>
        IsAdmin(context) ? query : query.Where(item => item.EventSession.Event.Organizer.OwnerUserId == CurrentUser(context).Id);

    private static User CurrentUser(HttpContext context) => (User)context.Items[LocalUserMiddleware.UserKey]!;
    private static bool IsAdmin(HttpContext context) => CurrentUser(context).Role == UserRole.Admin;

    private static ManagedTicketTypeResponse ToManagedResponse(TicketType item) => new(
        item.Id, item.EventSessionId, item.Name, item.Code, item.Description, item.Price, item.Currency,
        item.InventoryMode.ToString(), item.Capacity, item.MaxPerOrder, item.SaleStartsAtUtc,
        item.SaleEndsAtUtc, item.Version, item.CreatedAtUtc, item.UpdatedAtUtc,
        ToInventoryResponse(item.Inventory), item.Seats.OrderBy(seat => seat.Section)
            .ThenBy(seat => seat.RowLabel).ThenBy(seat => seat.SeatNumber).Select(ToSeatResponse).ToArray());

    private static InventoryResponse ToInventoryResponse(TicketInventory item) => new(
        item.Id, item.TotalQuantity, item.HeldQuantity, item.SoldQuantity, item.AvailableQuantity,
        item.Version, item.UpdatedAtUtc);

    private static SeatResponse ToSeatResponse(Seat item) => new(item.Id, item.EventSessionId, item.TicketTypeId,
        item.Section, item.RowLabel, item.SeatNumber, item.IsAccessible, item.IsActive,
        SeatStatus(item), item.CreatedAtUtc);

    private static SeatResponse ToPublicSeatResponse(Seat item) => ToSeatResponse(item);

    private static SeatMapResponse ToSeatMapResponse(TicketType item, bool includeInactive) => new(
        item.Id, item.Version, item.Capacity, item.Inventory.Version, item.Inventory.UpdatedAtUtc,
        item.Seats.Where(seat => includeInactive || seat.IsActive).OrderBy(seat => seat.Section)
            .ThenBy(seat => seat.RowLabel).ThenBy(seat => seat.SeatNumber).Select(ToSeatResponse).ToArray());

    private static TicketAvailabilityResponse ToAvailabilityResponse(TicketType item, DateTimeOffset snapshotAt) => new(
        item.Id, item.EventSessionId, item.Name, item.Code, item.Description, item.Price, item.Currency,
        item.InventoryMode.ToString(), item.Capacity, item.MaxPerOrder, item.SaleStartsAtUtc,
        item.SaleEndsAtUtc, item.Inventory.TotalQuantity, item.Inventory.HeldQuantity,
        item.Inventory.SoldQuantity, item.Inventory.AvailableQuantity, item.Inventory.Version,
        item.Inventory.UpdatedAtUtc, snapshotAt,
        item.InventoryMode == InventoryMode.ReservedSeating
            ? item.Seats.Where(seat => seat.IsActive).OrderBy(seat => seat.Section)
                .ThenBy(seat => seat.RowLabel).ThenBy(seat => seat.SeatNumber)
                .Select(ToPublicSeatResponse).ToArray()
            : []);

    private static string SeatStatus(Seat seat)
    {
        if (!seat.IsActive) return "Unavailable";
        if (seat.Allocations.Any(allocation => allocation.Status == SeatAllocationStatus.Sold)) return "Sold";
        if (seat.Allocations.Any(allocation => allocation.Status == SeatAllocationStatus.Held)) return "Held";
        return "Available";
    }

    private static bool IsActiveAllocation(SeatAllocation allocation) =>
        allocation.Status is SeatAllocationStatus.Held or SeatAllocationStatus.Sold;

    private static IResult? ValidateTicketTypeRequest(TicketTypeRequest request, SeatRequest[] seats)
    {
        if (request.Capacity <= 0) return BadRequest("INVALID_CAPACITY", "Capacity must be greater than zero.");
        if (request.InventoryMode == InventoryMode.GeneralAdmission && seats.Length > 0)
            return BadRequest("INVALID_SEAT_MAP", "General admission ticket types cannot contain seats.");
        if (request.InventoryMode == InventoryMode.ReservedSeating)
        {
            IResult? validation = ValidateSeatMap(seats);
            if (validation is not null) return validation;
            if (seats.Count(seat => seat.IsActive) != request.Capacity)
                return BadRequest("CAPACITY_SEAT_MISMATCH", "Reserved capacity must equal the number of active seats.");
        }
        return null;
    }

    private static IResult? ValidateSeatMap(IReadOnlyCollection<SeatRequest> seats)
    {
        if (seats.Count == 0) return BadRequest("EMPTY_SEAT_MAP", "A reserved ticket type needs at least one seat.");
        if (seats.Any(seat => string.IsNullOrWhiteSpace(seat.Section)
                || string.IsNullOrWhiteSpace(seat.RowLabel)
                || string.IsNullOrWhiteSpace(seat.SeatNumber)))
            return BadRequest("INVALID_SEAT", "Section, row and seat number are required.");
        string[] codes = seats.Select(seat => $"{seat.Section.Trim().ToUpperInvariant()}|{seat.RowLabel.Trim().ToUpperInvariant()}|{seat.SeatNumber.Trim().ToUpperInvariant()}").ToArray();
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
            return Conflict("SEAT_CODE_CONFLICT", "Seat codes must be unique within a session.");
        return null;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult VersionConflict(long currentVersion) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict, title: "Version conflict",
        detail: $"The current resource version is {currentVersion}.",
        extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT", ["currentVersion"] = currentVersion });

    private static IResult BadRequest(string code, string detail) => Problem(StatusCodes.Status400BadRequest, code, detail);
    private static IResult NotFound(string code, string detail) => Problem(StatusCodes.Status404NotFound, code, detail);
    private static IResult Conflict(string code, string detail) => Problem(StatusCodes.Status409Conflict, code, detail);
    private static IResult Problem(int status, string code, string detail) => Results.Problem(
        statusCode: status, title: code.Replace('_', ' '), detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record TicketTypeRequest(string Name, string Code, string? Description, decimal Price,
        InventoryMode InventoryMode, int Capacity, int MaxPerOrder = 10,
        DateTimeOffset? SaleStartsAtUtc = null, DateTimeOffset? SaleEndsAtUtc = null,
        SeatRequest[]? Seats = null);
    private sealed record TicketTypeUpdateRequest(string Name, string Code, string? Description, decimal Price,
        int MaxPerOrder, DateTimeOffset? SaleStartsAtUtc, DateTimeOffset? SaleEndsAtUtc, long Version);
    private sealed record CapacityRequest(int Capacity, long Version);
    private sealed record SeatMapRequest(long Version, SeatRequest[] Seats);
    private sealed record SeatRequest(Guid? Id, string Section, string RowLabel, string SeatNumber,
        bool IsAccessible = false, bool IsActive = true);

    private sealed record InventoryResponse(Guid Id, int TotalQuantity, int HeldQuantity, int SoldQuantity,
        int AvailableQuantity, long Version, DateTimeOffset UpdatedAtUtc);
    private sealed record SeatResponse(Guid Id, Guid EventSessionId, Guid TicketTypeId, string Section,
        string RowLabel, string SeatNumber, bool IsAccessible, bool IsActive, string Status,
        DateTimeOffset CreatedAtUtc);
    private sealed record ManagedTicketTypeResponse(Guid Id, Guid EventSessionId, string Name, string Code,
        string? Description, decimal Price, string Currency, string InventoryMode, int Capacity,
        int MaxPerOrder, DateTimeOffset? SaleStartsAtUtc, DateTimeOffset? SaleEndsAtUtc, long Version,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, InventoryResponse Inventory, SeatResponse[] Seats);
    private sealed record SeatMapResponse(Guid TicketTypeId, long Version, int Capacity, long InventoryVersion,
        DateTimeOffset UpdatedAtUtc, SeatResponse[] Seats);
    private sealed record TicketAvailabilityResponse(Guid Id, Guid EventSessionId, string Name, string Code,
        string? Description, decimal Price, string Currency, string InventoryMode, int Capacity,
        int MaxPerOrder, DateTimeOffset? SaleStartsAtUtc, DateTimeOffset? SaleEndsAtUtc,
        int TotalQuantity, int HeldQuantity, int SoldQuantity, int AvailableQuantity,
        long Version, DateTimeOffset UpdatedAtUtc, DateTimeOffset SnapshotAtUtc, SeatResponse[] Seats);
    private sealed record SessionAvailabilityResponse(Guid EventSessionId, long Version,
        DateTimeOffset SnapshotAtUtc, TicketAvailabilityResponse[] TicketTypes);
    private sealed record SeatAvailabilityResponse(Guid EventSessionId, Guid? TicketTypeId,
        DateTimeOffset SnapshotAtUtc, SeatResponse[] Seats);
}
