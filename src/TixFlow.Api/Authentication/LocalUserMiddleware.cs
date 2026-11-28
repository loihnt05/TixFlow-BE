using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using TixFlow.Application.Identity;
using TixFlow.Domain.Identity;

namespace TixFlow.Api.Authentication;

public sealed class LocalUserMiddleware(RequestDelegate next, IConfiguration configuration,
    ILogger<LocalUserMiddleware> logger)
{
    public static readonly object UserKey = new();
    public static readonly object ProfileKey = new();

    public async Task InvokeAsync(HttpContext context, ILocalUserSynchronizer synchronizer)
    {
        Endpoint? endpoint = context.GetEndpoint();
        bool requiresUser = (endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0
            || endpoint?.Metadata.GetOrderedMetadata<AuthorizationPolicy>().Count > 0)
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
        bool serviceCallback = endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(data => data.Policy == "MockPaymentService") == true;
        // Authorization runs first: denied requests never synchronize a profile.
        // Public routes, including health and Swagger, must not create/update users.
        if (requiresUser && !serviceCallback && context.User.Identity?.IsAuthenticated == true)
        {
            IdentityProfile? profile = TokenClaims.ReadProfile(context.User);
            if (profile is null)
            {
                await context.ForbidAsync(JwtBearerDefaults.AuthenticationScheme);
                return;
            }
            try
            {
                bool measure = configuration.GetValue<bool>("WaitingRoom:DiagnosticsEnabled")
                    && (context.Request.Path.StartsWithSegments("/api/v1/waiting-room")
                        || context.Request.Path.StartsWithSegments("/api/v1/holds"));
                Stopwatch? timer = measure ? Stopwatch.StartNew() : null;
                User user = await synchronizer.SynchronizeAsync(profile, context.RequestAborted);
                if (timer is not null)
                    logger.LogInformation("WaitingRoomTiming identity {Path} {ElapsedMs}",
                        context.Request.Path.Value, timer.Elapsed.TotalMilliseconds);
                if (user.Status != UserStatus.Active)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                context.Items[UserKey] = user;
                context.Items[ProfileKey] = profile;
            }
            catch (ProfileConflictException exception)
            {
                await Results.Problem(statusCode: 409, title: "Local profile conflict",
                    detail: exception.Message).ExecuteAsync(context);
                return;
            }
        }
        await next(context);
    }
}
