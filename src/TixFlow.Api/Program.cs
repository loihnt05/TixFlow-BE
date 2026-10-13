using TixFlow.Api.Modules.Assistant;
using TixFlow.Api.Modules.Booking;
using TixFlow.Api.Modules.Events;
using TixFlow.Application;
using TixFlow.Infrastructure;
using TixFlow.Infrastructure.Health;
using TixFlow.Api.Authentication;
using TixFlow.Api.Modules.Users;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<BookingOptions>()
    .Bind(builder.Configuration.GetSection(BookingOptions.SectionName))
    .Validate(options => options.HoldDurationMinutes is >= 1 and <= 60,
        "Booking hold duration must be between 1 and 60 minutes.")
    .Validate(options => options.LockTimeoutSeconds is >= 1 and <= 30,
        "Booking lock timeout must be between 1 and 30 seconds.")
    .Validate(options => options.IdempotencyRetentionHours is >= 1 and <= 168,
        "Booking idempotency retention must be between 1 and 168 hours.")
    .ValidateOnStart();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddIdentityAuthentication(builder.Configuration, builder.Environment);
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

// Match endpoint metadata before CORS, authentication and authorization.
app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
// Only authorized protected requests reach local profile/status checks, before the handler.
app.UseMiddleware<LocalUserMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "TixFlow API v1");
        options.OAuthClientId("tixflow-swagger");
        options.OAuthUsePkce();
        options.OAuthScopes("openid", "profile", "email");
    });
}
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
api.MapCatalogManagementModule();
api.MapInventoryModule();
api.MapBookingModule();
api.MapAssistantModule();
api.MapUsersModule();

app.Run();

public partial class Program
{
}
