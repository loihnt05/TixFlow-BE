<<<<<<< HEAD
using Microsoft.EntityFrameworkCore;
using TixFlow.Domain.Events;
using TixFlow.Infrastructure.Persistence;

=======
>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88
namespace TixFlow.Api.Modules.Events;

public static class EventsModule
{
    public static IEndpointRouteBuilder MapEventsModule(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/events").WithTags("Events");

<<<<<<< HEAD
        group.MapGet("/", GetEventsAsync);
=======
        group.MapGet("/", () => Results.Ok(new[]
        {
            new EventSummary(Guid.Parse("9b206fad-5025-48f2-88bd-f8e71d248429"), "TixFlow Flash Sale Demo", "Ho Chi Minh City")
        }));
>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88

        return endpoints;
    }

<<<<<<< HEAD
    private static async Task<IResult> GetEventsAsync(
        TixFlowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        EventSummary[] events = await dbContext.Events
            .AsNoTracking()
            .Where(item => item.Status == EventStatus.Published)
            .OrderBy(item => item.Name)
            .Select(item => new EventSummary(
                item.Id,
                item.Name,
                item.Slug,
                item.Category,
                item.Venue == null ? null : item.Venue.Name,
                item.Venue == null ? null : item.Venue.City,
                item.Sessions
                    .Where(session =>
                        session.Status == SessionStatus.Scheduled
                        || session.Status == SessionStatus.OnSale)
                    .OrderBy(session => session.StartsAtUtc)
                    .Select(session => (DateTimeOffset?)session.StartsAtUtc)
                    .FirstOrDefault()))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(events);
    }

    private sealed record EventSummary(
        Guid Id,
        string Name,
        string Slug,
        string Category,
        string? Venue,
        string? City,
        DateTimeOffset? NextSessionAtUtc);
}
=======
    private sealed record EventSummary(Guid Id, string Name, string Location);
}

>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88
