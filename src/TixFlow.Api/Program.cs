using TixFlow.Api.Modules.Assistant;
using TixFlow.Api.Modules.Booking;
using TixFlow.Api.Modules.Events;
using TixFlow.Application;
using TixFlow.Infrastructure;
using TixFlow.Infrastructure.Health;
using TixFlow.Api.Authentication;
using TixFlow.Api.Modules.Users;
using TixFlow.Api.Modules.Orders;
using TixFlow.Api.Modules.Tickets;
using System.Text.Json.Serialization;
using TixFlow.Api.Performance;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Serilog;
using Serilog.Formatting.Json;
using Prometheus;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((_, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext().WriteTo.Console(new JsonFormatter()));
builder.WebHost.ConfigureKestrel(options => builder.Configuration.GetSection("Kestrel:Limits").Bind(options.Limits));

builder.Services.AddApplication();
builder.Services.AddSingleton<CatalogCache>();
builder.Services.AddTrafficLimits(builder.Configuration);
builder.Services.AddAssistant(builder.Configuration);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    if (IPAddress.TryParse(builder.Configuration["ReverseProxy:KnownProxy"], out var proxy)) options.KnownProxies.Add(proxy);
});
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<MockPaymentSimulator>();
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
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddIdentityAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHttpClient("LoginToken");
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
// Direct deployments ignore forwarding headers. Enable only with an explicit trusted proxy.
if (IPAddress.TryParse(app.Configuration["ReverseProxy:KnownProxy"], out _)) app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-TixFlow-Instance"] = Environment.MachineName;
    await next(context);
});

// Match endpoint metadata before CORS, authentication and authorization.
app.UseRouting();
if (app.Configuration.GetValue<bool>("Monitoring:Enabled")) app.UseHttpMetrics();
// Only log route templates and server-generated correlation IDs, never query strings or bodies.
app.Use(async (context, next) =>
{
    var started = System.Diagnostics.Stopwatch.StartNew();
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    using (Serilog.Context.LogContext.PushProperty("CorrelationId", context.TraceIdentifier))
    {
        try { await next(context); }
        finally
        {
            Log.Information("HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs} ms",
                context.Request.Method,
                (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched",
                context.Response.StatusCode, started.Elapsed.TotalMilliseconds);
        }
    }
});
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
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
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
// Scrape endpoint exists only when enabled and is excluded from the public Nginx routes.
if (app.Configuration.GetValue<bool>("Monitoring:Enabled"))
    app.MapMetrics("/internal/metrics");

RouteGroupBuilder api = app.MapGroup("/api/v1");
if (app.Environment.IsDevelopment()) api.MapDevelopmentLogin();
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
api.MapOrdersModule();
api.MapTicketsModule();
api.MapAssistantModule();
api.MapUsersModule();
api.MapOperations();

app.Run();

public partial class Program
{
}
