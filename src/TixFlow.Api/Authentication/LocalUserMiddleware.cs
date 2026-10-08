using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using TixFlow.Application.Identity;
using TixFlow.Domain.Identity;

namespace TixFlow.Api.Authentication;

public sealed class LocalUserMiddleware(RequestDelegate next)
{
    public static readonly object UserKey = new();
    public static readonly object ProfileKey = new();

    public async Task InvokeAsync(HttpContext context, ILocalUserSynchronizer synchronizer)
    {
        Endpoint? endpoint = context.GetEndpoint();
        bool requiresUser = (endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0
            || endpoint?.Metadata.GetOrderedMetadata<AuthorizationPolicy>().Count > 0)
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
        // Authorization runs first: denied requests never synchronize a profile.
        // Public routes, including health and Swagger, must not create/update users.
        if (requiresUser && context.User.Identity?.IsAuthenticated == true)
        {
            IdentityProfile? profile = TokenClaims.ReadProfile(context.User);
            if (profile is null)
            {
                await context.ForbidAsync(JwtBearerDefaults.AuthenticationScheme);
                return;
            }
            try
            {
                User user = await synchronizer.SynchronizeAsync(profile, context.RequestAborted);
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
