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
                status = "database-ready",
                note = "The booking schema is ready; transactional hold APIs are the next implementation step."
            }));

        return endpoints;
    }
}
