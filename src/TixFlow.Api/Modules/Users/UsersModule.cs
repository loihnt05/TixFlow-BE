using Microsoft.EntityFrameworkCore;
using TixFlow.Api.Authentication;
using TixFlow.Application.Identity;
using TixFlow.Domain.Identity;
using TixFlow.Infrastructure.Persistence;

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
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(ToResponse(user, profile, user.PhoneNumber));
        }).Produces<CurrentUserResponse>();
        // Name and email are owned by Keycloak; only local contact details are editable here.
        group.MapPatch("/me", UpdateMeAsync)
            .Produces<CurrentUserResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        // Small access probes used by the role pages and Swagger; no business mutation is exposed.
        foreach (string role in TokenClaims.ApplicationRoles)
        {
            group.MapGet($"/access/{role.ToLowerInvariant()}", () => Results.Ok(new { role, allowed = true }))
                .RequireAuthorization(role);
        }
        return endpoints;
    }

    private static async Task<IResult> UpdateMeAsync(UpdateCurrentUserRequest? request, HttpContext context,
        TixFlowDbContext db, CancellationToken ct)
    {
        User user = (User)context.Items[LocalUserMiddleware.UserKey]!;
        IdentityProfile profile = (IdentityProfile)context.Items[LocalUserMiddleware.ProfileKey]!;
        context.Response.Headers.CacheControl = "no-store";
        if (request is null) return Validation("phoneNumber", "Request body is required.");

        // Blank clears the saved phone; anything else must be a plausible phone number.
        string? phoneNumber = PhoneNumbers.Normalize(request.PhoneNumber);
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && phoneNumber is null)
            return Validation("phoneNumber", "Phone number must contain 6–15 digits and only +, spaces, (, ), . or -.");

        await db.Users.Where(item => item.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.PhoneNumber, phoneNumber)
                .SetProperty(item => item.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
        return Results.Ok(ToResponse(user, profile, phoneNumber));
    }

    private static CurrentUserResponse ToResponse(User user, IdentityProfile profile, string? phoneNumber) => new(
        user.Id,
        profile.Subject,
        user.Email,
        user.DisplayName,
        [user.Role.ToString()],
        phoneNumber,
        user.Status.ToString(),
        user.CreatedAtUtc);

    private static IResult Validation(string field, string message) => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "VALIDATION_ERROR",
        detail: message,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "VALIDATION_ERROR",
            ["errors"] = new Dictionary<string, string[]> { [field] = [message] }
        });

    public sealed record UpdateCurrentUserRequest(string? PhoneNumber);

    private sealed record CurrentUserResponse(Guid Id, string Sub, string Email, string Name, string[] Roles,
        string? PhoneNumber, string Status, DateTimeOffset CreatedAtUtc);
}
