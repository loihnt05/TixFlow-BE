using TixFlow.Api.Modules.Assistant;
using TixFlow.Api.Modules.Booking;
using TixFlow.Api.Modules.Events;
using TixFlow.Application;
using TixFlow.Infrastructure;
using TixFlow.Infrastructure.Health;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres");
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
    version = "0.2.0",
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
