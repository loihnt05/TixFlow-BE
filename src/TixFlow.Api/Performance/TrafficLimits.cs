using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace TixFlow.Api.Performance;

public static class TrafficLimits
{
    public static IServiceCollection AddTrafficLimits(this IServiceCollection services, IConfiguration config)
    {
        services.AddRateLimiter(options =>
        {
            if (!config.GetValue("TrafficLimits:Enabled", true)) return;
            PartitionedRateLimiter<HttpContext> Limit(string dimension, Func<HttpContext, string> identity, int defaultLimit) =>
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    if (!context.Request.Path.StartsWithSegments("/api/v1")) return RateLimitPartition.GetNoLimiter("health");
                    string scope = context.Request.Path.StartsWithSegments("/api/v1/holds") ? "booking"
                        : HttpMethods.IsGet(context.Request.Method) ? "read" : "write";
                    int limit = Math.Clamp(config.GetValue($"TrafficLimits:{dimension}:{scope}", defaultLimit), 1, 100000);
                    return RateLimitPartition.GetFixedWindowLimiter(dimension + ":" + scope + ":" + identity(context), _ => new()
                    {
                        PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                    });
                });
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                Limit("Ip", c => c.Connection.RemoteIpAddress?.ToString() ?? "unknown", 600),
                Limit("User", c => c.User.FindFirst("sub")?.Value ?? c.Connection.RemoteIpAddress?.ToString() ?? "anonymous", 120),
                Limit("Client", c => c.User.FindFirst("azp")?.Value ?? "public", 3000));
            options.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                response.Headers.CacheControl = "no-store";
                response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
                    ? delay.TotalSeconds : 60)).ToString(CultureInfo.InvariantCulture);
                await response.WriteAsJsonAsync(new { code = "RATE_LIMITED", retryable = true }, ct);
            };
        });
        return services;
    }
}
