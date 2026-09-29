using TixFlow.Application.Identity;
using TixFlow.Domain.Identity;

namespace TixFlow.Api.Authentication;

public sealed class LocalUserMiddleware(RequestDelegate next)
{
    public static readonly object UserKey = new();
    public static readonly object ProfileKey = new();

    public async Task InvokeAsync(HttpContext context, ILocalUserSynchronizer synchronizer)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            IdentityProfile? profile = TokenClaims.ReadProfile(context.User);
            if (profile is null)
            {
                await Results.Problem(statusCode: 422, title: "Incomplete identity profile",
                    detail: "The identity provider must supply sub and email claims.").ExecuteAsync(context);
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
