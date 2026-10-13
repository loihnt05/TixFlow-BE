using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TixFlow.Api.Authentication;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;
using TixFlow.Domain.Organizers;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.Events;

public static class CatalogManagementModule
{
    public static IEndpointRouteBuilder MapCatalogManagementModule(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder management = endpoints.MapGroup("/management")
            .WithTags("Catalog Management")
            .RequireAuthorization("OrganizerOrAdmin");

        management.MapGet("/organizers/me", GetMyOrganizerAsync).RequireAuthorization("Organizer");
        management.MapPost("/organizers/me", CreateMyOrganizerAsync).RequireAuthorization("Organizer");
        management.MapPut("/organizers/me", UpdateMyOrganizerAsync).RequireAuthorization("Organizer");

        management.MapGet("/organizers", GetOrganizersAsync).RequireAuthorization("Admin");
        management.MapPost("/organizers", CreateOrganizerAsync).RequireAuthorization("Admin");
        management.MapGet("/organizers/{organizerId:guid}", GetOrganizerAsync).RequireAuthorization("Admin");
        management.MapPut("/organizers/{organizerId:guid}", UpdateOrganizerAsync).RequireAuthorization("Admin");
        management.MapDelete("/organizers/{organizerId:guid}", DeleteOrganizerAsync).RequireAuthorization("Admin");

        management.MapGet("/venues", GetVenuesAsync);
        management.MapPost("/venues", CreateVenueAsync);
        management.MapGet("/venues/{venueId:guid}", GetVenueAsync);
        management.MapPut("/venues/{venueId:guid}", UpdateVenueAsync);
        management.MapDelete("/venues/{venueId:guid}", DeleteVenueAsync);

        management.MapGet("/events", GetManagedEventsAsync);
        management.MapPost("/events", CreateEventAsync);
        management.MapGet("/events/{eventId:guid}", GetManagedEventAsync);
        management.MapPatch("/events/{eventId:guid}", UpdateEventAsync);
        management.MapDelete("/events/{eventId:guid}", DeleteEventAsync);
        management.MapPost("/events/{eventId:guid}/publish", PublishEventAsync);
        management.MapPost("/events/{eventId:guid}/cancellations", CancelEventAsync);
        management.MapPost("/events/{eventId:guid}/complete", CompleteEventAsync);

        management.MapGet("/events/{eventId:guid}/sessions", GetSessionsAsync);
        management.MapPost("/events/{eventId:guid}/sessions", CreateSessionAsync);
        management.MapGet("/event-sessions/{sessionId:guid}", GetSessionAsync);
        management.MapPut("/event-sessions/{sessionId:guid}", UpdateSessionAsync);
        management.MapDelete("/event-sessions/{sessionId:guid}", DeleteSessionAsync);

        return endpoints;
    }

    private static async Task<IResult> GetMyOrganizerAsync(
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        User user = CurrentUser(context);
        Organizer? organizer = await dbContext.Organizers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerUserId == user.Id, cancellationToken);
        return organizer is null ? NotFound("ORGANIZER_NOT_FOUND", "Organizer profile was not found.")
            : Results.Ok(ToResponse(organizer));
    }

    private static async Task<IResult> CreateMyOrganizerAsync(
        OrganizerRequest request,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        User user = CurrentUser(context);
        if (await dbContext.Organizers.AnyAsync(item => item.OwnerUserId == user.Id, cancellationToken))
            return Conflict("ORGANIZER_ALREADY_EXISTS", "This user already has an organizer profile.");

        return await CreateOrganizerCoreAsync(user.Id, request, dbContext, cancellationToken);
    }

    private static async Task<IResult> UpdateMyOrganizerAsync(
        OrganizerRequest request,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        User user = CurrentUser(context);
        Organizer? organizer = await dbContext.Organizers
            .SingleOrDefaultAsync(item => item.OwnerUserId == user.Id, cancellationToken);
        return organizer is null ? NotFound("ORGANIZER_NOT_FOUND", "Organizer profile was not found.")
            : await UpdateOrganizerCoreAsync(organizer, request, dbContext, cancellationToken);
    }

    private static async Task<IResult> GetOrganizersAsync(
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        OrganizerResponse[] organizers = await dbContext.Organizers.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new OrganizerResponse(
                item.Id, item.OwnerUserId, item.Name, item.Slug, item.Description,
                item.ContactEmail, item.CreatedAtUtc, item.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(organizers);
    }

    private static async Task<IResult> GetOrganizerAsync(
        Guid organizerId,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Organizer? organizer = await dbContext.Organizers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == organizerId, cancellationToken);
        return organizer is null ? NotFound("ORGANIZER_NOT_FOUND", "Organizer profile was not found.")
            : Results.Ok(ToResponse(organizer));
    }

    private static async Task<IResult> CreateOrganizerAsync(
        AdminOrganizerRequest request,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        User? owner = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == request.OwnerUserId, cancellationToken);
        if (owner is null || owner.Role != UserRole.Organizer)
            return BadRequest("INVALID_ORGANIZER_OWNER", "Owner must be an existing Organizer user.");
        if (await dbContext.Organizers.AnyAsync(item => item.OwnerUserId == owner.Id, cancellationToken))
            return Conflict("ORGANIZER_ALREADY_EXISTS", "This user already has an organizer profile.");
        return await CreateOrganizerCoreAsync(owner.Id, request.Profile, dbContext, cancellationToken);
    }

    private static async Task<IResult> UpdateOrganizerAsync(
        Guid organizerId,
        OrganizerRequest request,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Organizer? organizer = await dbContext.Organizers
            .SingleOrDefaultAsync(item => item.Id == organizerId, cancellationToken);
        return organizer is null ? NotFound("ORGANIZER_NOT_FOUND", "Organizer profile was not found.")
            : await UpdateOrganizerCoreAsync(organizer, request, dbContext, cancellationToken);
    }

    private static async Task<IResult> DeleteOrganizerAsync(
        Guid organizerId,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Organizer? organizer = await dbContext.Organizers
            .Include(item => item.Events).Include(item => item.Venues)
            .SingleOrDefaultAsync(item => item.Id == organizerId, cancellationToken);
        if (organizer is null) return NotFound("ORGANIZER_NOT_FOUND", "Organizer profile was not found.");
        if (organizer.Events.Count > 0 || organizer.Venues.Count > 0)
            return Conflict("ORGANIZER_IN_USE", "Organizer profiles with events or venues cannot be deleted.");
        dbContext.Organizers.Remove(organizer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateOrganizerCoreAsync(
        Guid ownerUserId,
        OrganizerRequest request,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string slug = Slug(request.Slug, request.Name);
        try
        {
            var organizer = new Organizer(ownerUserId, request.Name, slug, request.ContactEmail);
            organizer.Update(request.Name, slug, request.Description ?? string.Empty, request.ContactEmail);
            dbContext.Organizers.Add(organizer);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/management/organizers/{organizer.Id}", ToResponse(organizer));
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_ORGANIZER", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("ORGANIZER_CONFLICT", "Organizer slug or owner is already in use.");
        }
    }

    private static async Task<IResult> UpdateOrganizerCoreAsync(
        Organizer organizer,
        OrganizerRequest request,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            organizer.Update(request.Name, Slug(request.Slug, request.Name),
                request.Description ?? string.Empty, request.ContactEmail);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(organizer));
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_ORGANIZER", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("ORGANIZER_CONFLICT", "Organizer slug is already in use.");
        }
    }

    private static async Task<IResult> GetVenuesAsync(
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        IQueryable<Venue> query = Scope(dbContext.Venues.AsNoTracking(), context);
        Venue[] items = await query.OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        VenueResponse[] venues = items.Select(ToResponse).ToArray();
        return Results.Ok(venues);
    }

    private static async Task<IResult> GetVenueAsync(
        Guid venueId,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Venue? venue = await Scope(dbContext.Venues.AsNoTracking(), context)
            .SingleOrDefaultAsync(item => item.Id == venueId, cancellationToken);
        return venue is null ? NotFound("VENUE_NOT_FOUND", "Venue was not found.") : Results.Ok(ToResponse(venue));
    }

    private static async Task<IResult> CreateVenueAsync(
        VenueRequest request,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Guid? organizerId = await ResolveOrganizerIdAsync(request.OrganizerId, context, dbContext, cancellationToken);
        if (!organizerId.HasValue) return BadRequest("ORGANIZER_REQUIRED", "Create an organizer profile first.");
        try
        {
            var venue = new Venue(organizerId.Value, request.Name, request.AddressLine, request.City);
            venue.Update(request.Name, request.AddressLine, request.Ward, request.District, request.City,
                request.CountryCode ?? "VN", request.TimeZone ?? "Asia/Ho_Chi_Minh");
            dbContext.Venues.Add(venue);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/management/venues/{venue.Id}", ToResponse(venue));
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_VENUE", exception.Message);
        }
    }

    private static async Task<IResult> UpdateVenueAsync(
        Guid venueId,
        VenueRequest request,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Venue? venue = await Scope(dbContext.Venues, context)
            .SingleOrDefaultAsync(item => item.Id == venueId, cancellationToken);
        if (venue is null) return NotFound("VENUE_NOT_FOUND", "Venue was not found.");
        try
        {
            venue.Update(request.Name, request.AddressLine, request.Ward, request.District, request.City,
                request.CountryCode ?? "VN", request.TimeZone ?? "Asia/Ho_Chi_Minh");
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(venue));
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_VENUE", exception.Message);
        }
    }

    private static async Task<IResult> DeleteVenueAsync(
        Guid venueId,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Venue? venue = await Scope(dbContext.Venues, context)
            .SingleOrDefaultAsync(item => item.Id == venueId, cancellationToken);
        if (venue is null) return NotFound("VENUE_NOT_FOUND", "Venue was not found.");
        dbContext.Venues.Remove(venue);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetManagedEventsAsync(
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Event[] items = await Scope(ManagedEvents(dbContext), context)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(items.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> GetManagedEventAsync(
        Guid eventId,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Event? item = await Scope(ManagedEvents(dbContext), context)
            .SingleOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken);
        return item is null ? NotFound("EVENT_NOT_FOUND", "Event was not found.") : Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> CreateEventAsync(
        EventRequest request,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Guid? organizerId = await ResolveOrganizerIdAsync(request.OrganizerId, context, dbContext, cancellationToken);
        if (!organizerId.HasValue) return BadRequest("ORGANIZER_REQUIRED", "Create an organizer profile first.");
        if (request.VenueId.HasValue && !await dbContext.Venues.AnyAsync(
                item => item.Id == request.VenueId && item.OrganizerId == organizerId, cancellationToken))
            return BadRequest("INVALID_VENUE", "Venue must belong to the event organizer.");

        string slug = Slug(request.Slug, request.Name);
        try
        {
            var item = new Event(organizerId.Value, request.Name, slug, request.Category);
            item.Update(request.Name, slug, request.Description ?? string.Empty, request.Category,
                request.VenueId, request.ImageUrl, request.SaleStartsAtUtc, request.SaleEndsAtUtc);
            dbContext.Events.Add(item);
            await dbContext.SaveChangesAsync(cancellationToken);
            Event created = await ManagedEvents(dbContext).SingleAsync(candidate => candidate.Id == item.Id, cancellationToken);
            return Results.Created($"/api/v1/management/events/{item.Id}", ToResponse(created));
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_EVENT", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("EVENT_SLUG_CONFLICT", "This organizer already uses the event slug.");
        }
    }

    private static async Task<IResult> UpdateEventAsync(
        Guid eventId,
        EventRequest request,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Event? item = await Scope(dbContext.Events.Include(candidate => candidate.Sessions), context)
            .SingleOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken);
        if (item is null) return NotFound("EVENT_NOT_FOUND", "Event was not found.");
        if (item.Version != request.Version) return VersionConflict(item.Version);
        if (request.VenueId.HasValue && !await dbContext.Venues.AnyAsync(
                venue => venue.Id == request.VenueId && venue.OrganizerId == item.OrganizerId, cancellationToken))
            return BadRequest("INVALID_VENUE", "Venue must belong to the event organizer.");
        try
        {
            item.Update(request.Name, Slug(request.Slug, request.Name), request.Description ?? string.Empty,
                request.Category, request.VenueId, request.ImageUrl, request.SaleStartsAtUtc, request.SaleEndsAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            Event updated = await ManagedEvents(dbContext).SingleAsync(candidate => candidate.Id == item.Id, cancellationToken);
            return Results.Ok(ToResponse(updated));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict("INVALID_EVENT_STATE", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_EVENT", exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("VERSION_CONFLICT", "The event was changed by another request.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("EVENT_SLUG_CONFLICT", "This organizer already uses the event slug.");
        }
    }

    private static async Task<IResult> DeleteEventAsync(
        Guid eventId,
        long version,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Event? item = await Scope(dbContext.Events
                .Include(candidate => candidate.Sessions).ThenInclude(session => session.Seats), context)
            .SingleOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken);
        if (item is null) return NotFound("EVENT_NOT_FOUND", "Event was not found.");
        if (item.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Only draft events can be deleted.");
        if (item.Version != version) return VersionConflict(item.Version);
        Guid[] seatIds = item.Sessions.SelectMany(session => session.Seats).Select(seat => seat.Id).ToArray();
        if (seatIds.Length > 0 && (await dbContext.SeatAllocations.AnyAsync(
                allocation => seatIds.Contains(allocation.SeatId), cancellationToken)
            || await dbContext.Tickets.AnyAsync(ticket => ticket.SeatId.HasValue
                && seatIds.Contains(ticket.SeatId.Value), cancellationToken)))
            return Conflict("EVENT_IN_USE", "An event with seat booking history cannot be deleted.");
        dbContext.Seats.RemoveRange(item.Sessions.SelectMany(session => session.Seats));
        dbContext.Events.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> PublishEventAsync(Guid eventId, VersionRequest request, HttpContext context,
        TixFlowDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        IQueryable<Event> query = dbContext.Events
            .Include(item => item.Organizer)
            .Include(item => item.Sessions).ThenInclude(session => session.TicketTypes)
                .ThenInclude(ticket => ticket.Inventory)
            .Include(item => item.Sessions).ThenInclude(session => session.TicketTypes)
                .ThenInclude(ticket => ticket.Seats);
        Event? item = await Scope(query, context)
            .AsSplitQuery().SingleOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken);
        if (item is null) return NotFound("EVENT_NOT_FOUND", "Event was not found.");
        if (item.Version != request.Version) return VersionConflict(item.Version);
        try
        {
            item.Publish(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            Event updated = await ManagedEvents(dbContext)
                .SingleAsync(candidate => candidate.Id == item.Id, cancellationToken);
            return Results.Ok(ToResponse(updated));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict("INVALID_EVENT_STATE", exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("VERSION_CONFLICT", "The event was changed by another request.");
        }
    }

    private static Task<IResult> CancelEventAsync(Guid eventId, CancellationRequest request, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Task.FromResult(BadRequest("CANCELLATION_REASON_REQUIRED", "Cancellation reason is required."));
        return TransitionEventAsync(eventId, request.Version, context, dbContext, cancellationToken,
            item => item.Cancel());
    }

    private static Task<IResult> CompleteEventAsync(Guid eventId, VersionRequest request, HttpContext context,
        TixFlowDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        TransitionEventAsync(eventId, request.Version, context, dbContext, cancellationToken,
            item => item.Complete(timeProvider.GetUtcNow()));

    private static async Task<IResult> TransitionEventAsync(
        Guid eventId,
        long version,
        HttpContext context,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken,
        Action<Event> transition)
    {
        Event? item = await Scope(dbContext.Events.Include(candidate => candidate.Sessions), context)
            .SingleOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken);
        if (item is null) return NotFound("EVENT_NOT_FOUND", "Event was not found.");
        if (item.Version != version) return VersionConflict(item.Version);
        try
        {
            transition(item);
            await dbContext.SaveChangesAsync(cancellationToken);
            Event updated = await ManagedEvents(dbContext).SingleAsync(candidate => candidate.Id == item.Id, cancellationToken);
            return Results.Ok(ToResponse(updated));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict("INVALID_EVENT_STATE", exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("VERSION_CONFLICT", "The event was changed by another request.");
        }
    }

    private static async Task<IResult> GetSessionsAsync(Guid eventId, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await Scope(dbContext.Events, context).AnyAsync(item => item.Id == eventId, cancellationToken))
            return NotFound("EVENT_NOT_FOUND", "Event was not found.");
        EventSession[] items = await dbContext.EventSessions.AsNoTracking()
            .Include(item => item.TicketTypes).ThenInclude(ticket => ticket.Inventory)
            .Where(item => item.EventId == eventId).OrderBy(item => item.StartsAtUtc)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(items.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> CreateSessionAsync(Guid eventId, SessionRequest request, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        Event? item = await Scope(dbContext.Events, context)
            .SingleOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken);
        if (item is null) return NotFound("EVENT_NOT_FOUND", "Event was not found.");
        if (item.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Sessions can only be added to draft events.");
        try
        {
            var session = new EventSession(eventId, request.Name, request.StartsAtUtc, request.EndsAtUtc);
            dbContext.EventSessions.Add(session);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/management/event-sessions/{session.Id}", ToResponse(session));
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_SESSION", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("SESSION_TIME_CONFLICT", "This event already has a session at the selected start time.");
        }
    }

    private static async Task<IResult> GetSessionAsync(Guid sessionId, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        EventSession? session = await ScopeSessions(dbContext.EventSessions.AsNoTracking()
                .Include(item => item.TicketTypes).ThenInclude(ticket => ticket.Inventory), context)
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        return session is null ? NotFound("SESSION_NOT_FOUND", "Session was not found.") : Results.Ok(ToResponse(session));
    }

    private static async Task<IResult> UpdateSessionAsync(Guid sessionId, SessionRequest request, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        EventSession? session = await ScopeSessions(dbContext.EventSessions.Include(item => item.Event), context)
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        if (session is null) return NotFound("SESSION_NOT_FOUND", "Session was not found.");
        if (session.Version != request.Version) return VersionConflict(session.Version);
        if (session.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Sessions can only be edited while the event is a draft.");
        try
        {
            session.Update(request.Name, request.StartsAtUtc, request.EndsAtUtc);
            if (request.Status.HasValue && request.Status != session.Status)
                session.SetStatus(request.Status.Value, DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(session));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict("INVALID_SESSION_STATE", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return BadRequest("INVALID_SESSION", exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("VERSION_CONFLICT", "The session was changed by another request.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("SESSION_TIME_CONFLICT", "This event already has a session at the selected start time.");
        }
    }

    private static async Task<IResult> DeleteSessionAsync(Guid sessionId, long version, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        EventSession? session = await ScopeSessions(dbContext.EventSessions.Include(item => item.Event)
                .Include(item => item.Seats), context)
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        if (session is null) return NotFound("SESSION_NOT_FOUND", "Session was not found.");
        if (session.Version != version) return VersionConflict(session.Version);
        if (session.Event.Status != EventStatus.Draft)
            return Conflict("INVALID_EVENT_STATE", "Sessions can only be deleted while the event is a draft.");
        Guid[] seatIds = session.Seats.Select(seat => seat.Id).ToArray();
        if (seatIds.Length > 0 && (await dbContext.SeatAllocations.AnyAsync(
                allocation => seatIds.Contains(allocation.SeatId), cancellationToken)
            || await dbContext.Tickets.AnyAsync(ticket => ticket.SeatId.HasValue
                && seatIds.Contains(ticket.SeatId.Value), cancellationToken)))
            return Conflict("SESSION_IN_USE", "A session with seat booking history cannot be deleted.");
        dbContext.Seats.RemoveRange(session.Seats);
        dbContext.EventSessions.Remove(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static IQueryable<Event> ManagedEvents(TixFlowDbContext dbContext) => dbContext.Events.AsNoTracking()
        .Include(item => item.Organizer).Include(item => item.Venue)
        .Include(item => item.Sessions).ThenInclude(session => session.TicketTypes)
            .ThenInclude(ticket => ticket.Inventory)
        .AsSplitQuery();

    private static IQueryable<Event> Scope(IQueryable<Event> query, HttpContext context) =>
        IsAdmin(context) ? query : query.Where(item => item.Organizer.OwnerUserId == CurrentUser(context).Id);

    private static IQueryable<Venue> Scope(IQueryable<Venue> query, HttpContext context) =>
        IsAdmin(context) ? query : query.Where(item => item.Organizer.OwnerUserId == CurrentUser(context).Id);

    private static IQueryable<EventSession> ScopeSessions(IQueryable<EventSession> query, HttpContext context) =>
        IsAdmin(context) ? query : query.Where(item => item.Event.Organizer.OwnerUserId == CurrentUser(context).Id);

    private static async Task<Guid?> ResolveOrganizerIdAsync(Guid? requestedId, HttpContext context,
        TixFlowDbContext dbContext, CancellationToken cancellationToken)
    {
        if (IsAdmin(context))
            return requestedId.HasValue && await dbContext.Organizers.AnyAsync(item => item.Id == requestedId, cancellationToken)
                ? requestedId : null;
        Guid userId = CurrentUser(context).Id;
        return await dbContext.Organizers.Where(item => item.OwnerUserId == userId)
            .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken);
    }

    private static User CurrentUser(HttpContext context) =>
        (User)context.Items[LocalUserMiddleware.UserKey]!;

    private static bool IsAdmin(HttpContext context) => CurrentUser(context).Role == UserRole.Admin;

    private static OrganizerResponse ToResponse(Organizer item) => new(
        item.Id, item.OwnerUserId, item.Name, item.Slug, item.Description,
        item.ContactEmail, item.CreatedAtUtc, item.UpdatedAtUtc);

    private static VenueResponse ToResponse(Venue item) => new(
        item.Id, item.OrganizerId, item.Name, item.AddressLine, item.Ward, item.District,
        item.City, item.CountryCode, item.TimeZone, item.CreatedAtUtc, item.UpdatedAtUtc);

    private static EventResponse ToResponse(Event item) => new(
        item.Id, item.OrganizerId, item.Name, item.Slug, item.Description, item.Category,
        item.ImageUrl, item.Status.ToString(), item.SaleStartsAtUtc, item.SaleEndsAtUtc,
        item.PublishedAtUtc, item.Version, item.CreatedAtUtc, item.UpdatedAtUtc,
        item.Organizer is null ? null : ToResponse(item.Organizer),
        item.Venue is null ? null : ToResponse(item.Venue),
        item.Sessions.OrderBy(session => session.StartsAtUtc).Select(ToResponse).ToArray());

    private static SessionResponse ToResponse(EventSession item) => new(
        item.Id, item.EventId, item.Name, item.StartsAtUtc, item.EndsAtUtc,
        item.Status.ToString(), item.Version, item.CreatedAtUtc, item.UpdatedAtUtc,
        item.TicketTypes.OrderBy(ticket => ticket.Price).Select(ticket => new TicketSummaryResponse(
            ticket.Id, ticket.Name, ticket.Code, ticket.Price, ticket.Currency,
            ticket.InventoryMode.ToString(), ticket.Capacity,
            ticket.Inventory is null ? 0 : ticket.Inventory.AvailableQuantity,
            ticket.MaxPerOrder, ticket.Version)).ToArray());

    private static string Slug(string? value, string fallback)
    {
        string normalized = (string.IsNullOrWhiteSpace(value) ? fallback : value)
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        bool pendingDash = false;
        foreach (char character in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingDash && builder.Length > 0) builder.Append('-');
                builder.Append(char.ToLowerInvariant(character));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }
        string result = builder.ToString();
        if (string.IsNullOrWhiteSpace(result)) throw new ArgumentException("A valid slug is required.");
        return result;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult VersionConflict(long currentVersion) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Version conflict",
        detail: $"The current resource version is {currentVersion}.",
        extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT", ["currentVersion"] = currentVersion });

    private static IResult BadRequest(string code, string detail) => Problem(StatusCodes.Status400BadRequest, code, detail);
    private static IResult NotFound(string code, string detail) => Problem(StatusCodes.Status404NotFound, code, detail);
    private static IResult Conflict(string code, string detail) => Problem(StatusCodes.Status409Conflict, code, detail);
    private static IResult Problem(int status, string code, string detail) => Results.Problem(
        statusCode: status, title: code.Replace('_', ' '), detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record OrganizerRequest(string Name, string? Slug, string? Description, string ContactEmail);
    private sealed record AdminOrganizerRequest(Guid OwnerUserId, OrganizerRequest Profile);
    private sealed record VenueRequest(Guid? OrganizerId, string Name, string AddressLine, string? Ward,
        string? District, string City, string? CountryCode, string? TimeZone);
    private sealed record EventRequest(Guid? OrganizerId, string Name, string? Slug, string? Description,
        string Category, Guid? VenueId, string? ImageUrl, DateTimeOffset? SaleStartsAtUtc,
        DateTimeOffset? SaleEndsAtUtc, long Version = 0);
    private sealed record SessionRequest(string Name, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
        SessionStatus? Status = null, long Version = 0);
    private sealed record VersionRequest(long Version);
    private sealed record CancellationRequest(long Version, string Reason);

    private sealed record OrganizerResponse(Guid Id, Guid OwnerUserId, string Name, string Slug,
        string Description, string ContactEmail, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
    private sealed record VenueResponse(Guid Id, Guid OrganizerId, string Name, string AddressLine,
        string? Ward, string? District, string City, string CountryCode, string TimeZone,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
    private sealed record SessionResponse(Guid Id, Guid EventId, string Name, DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc, string Status, long Version, DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc, TicketSummaryResponse[] TicketTypes);
    private sealed record TicketSummaryResponse(Guid Id, string Name, string Code, decimal Price,
        string Currency, string InventoryMode, int Capacity, int AvailableQuantity,
        int MaxPerOrder, long Version);
    private sealed record EventResponse(Guid Id, Guid OrganizerId, string Name, string Slug,
        string Description, string Category, string? ImageUrl, string Status,
        DateTimeOffset? SaleStartsAtUtc, DateTimeOffset? SaleEndsAtUtc, DateTimeOffset? PublishedAtUtc,
        long Version, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
        OrganizerResponse? Organizer, VenueResponse? Venue, SessionResponse[] Sessions);
}
