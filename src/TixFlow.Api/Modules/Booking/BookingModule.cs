namespace TixFlow.Api.Modules.Booking;

public static class BookingModule
{
    public static IEndpointRouteBuilder MapBookingModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/bookings")
            .WithTags("Booking")
            .MapGet("/status", () => Results.Ok(new
            {
                module = "Booking",
                status = "skeleton-ready",
                note = "Seat-hold and concurrency logic are intentionally not implemented yet."
            }));

        return endpoints;
    }
}

