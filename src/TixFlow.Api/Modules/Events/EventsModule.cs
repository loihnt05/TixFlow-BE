using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TixFlow.Domain.Events;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.Events;

public static class EventsModule
{
    private static readonly Expression<Func<Event, DateTimeOffset?>> NextSessionExpression = item => item.Sessions
        .Where(session => session.Status == SessionStatus.Scheduled
            || session.Status == SessionStatus.OnSale
            || session.Status == SessionStatus.SoldOut)
        .Select(session => (DateTimeOffset?)session.StartsAtUtc)
        .Min();

    public static IEndpointRouteBuilder MapEventsModule(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/events").WithTags("Events");

        group.MapGet("/", GetEventsAsync)
            .WithSummary("List published events")
            .WithDescription("Returns a stable, paged public catalog. Draft, cancelled and completed events are never exposed.");
        group.MapGet("/{identifier}", GetEventAsync)
            .WithSummary("Get a published event by slug or id");

        return endpoints;
    }

    private static async Task<IResult> GetEventsAsync(
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken,
        string? q = null,
        string? category = null,
        string? location = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? status = null,
        string? sortBy = null,
        int page = 1,
        int limit = 12)
    {
        page = page <= 0 ? 1 : page;
        limit = limit <= 0 ? 12 : Math.Min(limit, 100);
        if (dateFrom.HasValue && dateTo.HasValue && dateTo < dateFrom)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(dateTo)] = ["dateTo must be on or after dateFrom."]
            });

        IQueryable<Event> query = dbContext.Events.AsNoTracking()
            .Where(item => item.Status == EventStatus.Published);

        if (!string.IsNullOrWhiteSpace(status)
            && !status.Equals(nameof(EventStatus.Published), StringComparison.OrdinalIgnoreCase))
            return Results.Ok(new PublicEventPage([], 0, page, limit, 0));

        if (!string.IsNullOrWhiteSpace(q))
        {
            string pattern = $"%{q.Trim()}%";
            query = query.Where(item =>
                EF.Functions.ILike(item.Name, pattern)
                || EF.Functions.ILike(item.Description, pattern)
                || EF.Functions.ILike(item.Organizer.Name, pattern)
                || (item.Venue != null && EF.Functions.ILike(item.Venue.Name, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(item => EF.Functions.ILike(item.Category, category.Trim()));

        if (!string.IsNullOrWhiteSpace(location))
        {
            string pattern = $"%{location.Trim()}%";
            query = query.Where(item => item.Venue != null
                && (EF.Functions.ILike(item.Venue.City, pattern)
                    || EF.Functions.ILike(item.Venue.Name, pattern)
                    || EF.Functions.ILike(item.Venue.AddressLine, pattern)));
        }

        if (dateFrom.HasValue && dateTo.HasValue)
        {
            DateTimeOffset start = UtcStart(dateFrom.Value);
            DateTimeOffset endExclusive = UtcStart(dateTo.Value.AddDays(1));
            query = query.Where(item => item.Sessions.Any(session =>
                session.StartsAtUtc >= start && session.StartsAtUtc < endExclusive));
        }
        else if (dateFrom.HasValue)
        {
            DateTimeOffset start = UtcStart(dateFrom.Value);
            query = query.Where(item => item.Sessions.Any(session => session.StartsAtUtc >= start));
        }
        else if (dateTo.HasValue)
        {
            DateTimeOffset endExclusive = UtcStart(dateTo.Value.AddDays(1));
            query = query.Where(item => item.Sessions.Any(session => session.StartsAtUtc < endExclusive));
        }

        int total = await query.CountAsync(cancellationToken);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "date_desc" => query.OrderByDescending(NextSessionExpression).ThenBy(item => item.Id),
            "newest" => query.OrderByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id),
            "name" => query.OrderBy(item => item.Name).ThenBy(item => item.Id),
            _ => query.OrderBy(NextSessionExpression).ThenBy(item => item.Id)
        };

        Event[] events = await WithPublicGraph(query)
            .Skip((page - 1) * limit)
            .Take(limit)
            .AsSplitQuery()
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new PublicEventPage(
            events.Select(ToResponse).ToArray(), total, page, limit,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)limit)));
    }

    private static async Task<IResult> GetEventAsync(
        string identifier,
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        IQueryable<Event> query = WithPublicGraph(dbContext.Events.AsNoTracking())
            .Where(item => item.Status == EventStatus.Published);

        Event? item = Guid.TryParse(identifier, out Guid id)
            ? await query.AsSplitQuery().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            : await query.AsSplitQuery().SingleOrDefaultAsync(
                candidate => candidate.Slug == identifier.Trim().ToLower(), cancellationToken);

        return item is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "EVENT_NOT_FOUND",
                detail: "Published event was not found.")
            : Results.Ok(ToResponse(item));
    }

    private static IQueryable<Event> WithPublicGraph(IQueryable<Event> query) => query
        .Include(item => item.Organizer)
        .Include(item => item.Venue)
        .Include(item => item.Sessions)
            .ThenInclude(session => session.TicketTypes)
            .ThenInclude(ticketType => ticketType.Inventory);

    private static bool IsPublicSession(EventSession session) =>
        session.Status is SessionStatus.Scheduled or SessionStatus.OnSale or SessionStatus.SoldOut;

    private static DateTimeOffset UtcStart(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static PublicEventResponse ToResponse(Event item)
    {
        PublicSessionResponse[] sessions = item.Sessions
            .Where(IsPublicSession)
            .OrderBy(session => session.StartsAtUtc)
            .ThenBy(session => session.Id)
            .Select(session => new PublicSessionResponse(
                session.Id,
                session.Name,
                session.StartsAtUtc,
                session.EndsAtUtc,
                session.Status.ToString(),
                session.TicketTypes.OrderBy(ticket => ticket.Price).ThenBy(ticket => ticket.Id)
                    .Select(ticket => new PublicTicketTypeResponse(
                        ticket.Id,
                        ticket.Name,
                        ticket.Description ?? string.Empty,
                        ticket.Price,
                        ticket.Currency,
                        ticket.Capacity,
                        ticket.Inventory is null ? 0 : ticket.Inventory.SoldQuantity,
                        ticket.MaxPerOrder,
                        ticket.SaleStartsAtUtc,
                        ticket.SaleEndsAtUtc))
                    .ToArray()))
            .ToArray();

        return new PublicEventResponse(
            item.Id,
            item.Name,
            item.Slug,
            item.Description,
            item.Category,
            item.ImageUrl,
            item.Status.ToString(),
            item.SaleStartsAtUtc,
            item.SaleEndsAtUtc,
            item.PublishedAtUtc,
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            new PublicOrganizerResponse(
                item.Organizer.Id, item.Organizer.Name, item.Organizer.Slug,
                item.Organizer.Description, 0),
            item.Venue is null ? null : new PublicVenueResponse(
                item.Venue.Id, item.Venue.Name, item.Venue.AddressLine, item.Venue.Ward,
                item.Venue.District, item.Venue.City, item.Venue.CountryCode, item.Venue.TimeZone),
            sessions);
    }

    private sealed record PublicEventPage(
        PublicEventResponse[] Events, int Total, int Page, int Limit, int TotalPages);

    private sealed record PublicEventResponse(
        Guid Id, string Name, string Slug, string Description, string Category, string? ImageUrl,
        string Status, DateTimeOffset? SaleStartsAtUtc, DateTimeOffset? SaleEndsAtUtc,
        DateTimeOffset? PublishedAtUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
        PublicOrganizerResponse Organizer, PublicVenueResponse? Venue, PublicSessionResponse[] Sessions);

    private sealed record PublicOrganizerResponse(
        Guid Id, string Name, string Slug, string Description, int EventCount);

    private sealed record PublicVenueResponse(
        Guid Id, string Name, string AddressLine, string? Ward, string? District,
        string City, string CountryCode, string TimeZone);

    private sealed record PublicSessionResponse(
        Guid Id, string Name, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
        string Status, PublicTicketTypeResponse[] TicketTypes);

    private sealed record PublicTicketTypeResponse(
        Guid Id, string Name, string Description, decimal Price, string Currency,
        int Quantity, int Sold, int MaxPerOrder,
        DateTimeOffset? SaleStartsAtUtc, DateTimeOffset? SaleEndsAtUtc);
}
