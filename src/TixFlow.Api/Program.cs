using TixFlow.Api.Modules.Assistant;
using TixFlow.Api.Modules.Booking;
using TixFlow.Api.Modules.Events;
using TixFlow.Application;
using TixFlow.Infrastructure;
using TixFlow.Infrastructure.Health;
using TixFlow.Api.Authentication;
using TixFlow.Api.Modules.Users;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
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

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
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
api.MapBookingModule();
api.MapAssistantModule();
api.MapUsersModule();

app.Run();

public partial class Program
{
}
