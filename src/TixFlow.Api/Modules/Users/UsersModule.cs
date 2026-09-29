using TixFlow.Api.Authentication;
using TixFlow.Application.Identity;
using TixFlow.Domain.Identity;

namespace TixFlow.Api.Modules.Users;

public static class UsersModule
{
    public static IEndpointRouteBuilder MapUsersModule(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/users").WithTags("Users").RequireAuthorization();
        group.MapGet("/me", (HttpContext context) =>
        {
            User user = (User)context.Items[LocalUserMiddleware.UserKey]!;
            IdentityProfile profile = (IdentityProfile)context.Items[LocalUserMiddleware.ProfileKey]!;
            return Results.Ok(new { user.Id, sub = profile.Subject, profile.Email, profile.Name, profile.Roles });
        });
        // Small access probes used by the role pages and Swagger; no business mutation is exposed.
        foreach (string role in TokenClaims.ApplicationRoles)
        {
            group.MapGet($"/access/{role.ToLowerInvariant()}", () => Results.Ok(new { role, allowed = true }))
                .RequireAuthorization(role);
        }
        return endpoints;
    }
}
