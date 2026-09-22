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
<<<<<<< HEAD
                status = "database-ready",
                note = "The booking schema is ready; transactional hold APIs are the next implementation step."
=======
                status = "skeleton-ready",
                note = "Seat-hold and concurrency logic are intentionally not implemented yet."
>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88
            }));

        return endpoints;
    }
}
<<<<<<< HEAD
=======

>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88
