namespace TixFlow.Api.Modules.Events;

public static class EventsModule
{
    public static IEndpointRouteBuilder MapEventsModule(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/events").WithTags("Events");

        group.MapGet("/", () => Results.Ok(new[]
        {
            new EventSummary(Guid.Parse("9b206fad-5025-48f2-88bd-f8e71d248429"), "TixFlow Flash Sale Demo", "Ho Chi Minh City")
        }));

        return endpoints;
    }

    private sealed record EventSummary(Guid Id, string Name, string Location);
}

