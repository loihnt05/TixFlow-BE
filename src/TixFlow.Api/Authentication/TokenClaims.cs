using System.Security.Claims;
using TixFlow.Application.Identity;

namespace TixFlow.Api.Authentication;

public static class TokenClaims
{
    public static readonly string[] ApplicationRoles = ["Admin", "Organizer", "Customer"];

    public static IdentityProfile? ReadProfile(ClaimsPrincipal principal)
    {
        string? subject = principal.FindFirstValue("sub");
        string? email = principal.FindFirstValue("email")?.Trim();
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255
            || string.IsNullOrWhiteSpace(email) || email.Length > 320)
        {
            return null;
        }

        string name = principal.FindFirstValue("name")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = principal.FindFirstValue("preferred_username")?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) name = email;
        }

        string[] roles = principal.FindAll("roles").Select(claim => claim.Value)
            .Where(role => ApplicationRoles.Contains(role, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        return new IdentityProfile(subject, email, name[..Math.Min(name.Length, 120)], roles);
    }
}
