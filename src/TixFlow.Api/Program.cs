using TixFlow.Api.Modules.Assistant;
using TixFlow.Api.Modules.Booking;
using TixFlow.Api.Modules.Events;
using TixFlow.Application;
using TixFlow.Infrastructure;
<<<<<<< HEAD
using TixFlow.Infrastructure.Health;
=======
>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
<<<<<<< HEAD
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres");
=======
builder.Services.AddHealthChecks();
>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

WebApplication app = builder.Build();

app.UseCors("Frontend");
app.MapHealthChecks("/health");

RouteGroupBuilder api = app.MapGroup("/api/v1");
api.MapGet("/system/info", (IHostEnvironment environment) => Results.Ok(new
{
    application = "TixFlow.Api",
<<<<<<< HEAD
    version = "0.2.0",
=======
    version = "0.1.0",
>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88
    environment = environment.EnvironmentName,
    utcNow = DateTimeOffset.UtcNow
}));

api.MapEventsModule();
api.MapBookingModule();
api.MapAssistantModule();

app.Run();

public partial class Program
{
}
