using System.Security.Claims;
using TixFlow.Application.Identity;

namespace TixFlow.Api.Authentication;

public static class TokenClaims
{
    public const string ApiAudience = "tixflow-api";
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Username = "preferred_username";
    public const string Roles = "roles";

    public static IReadOnlyList<string> ApplicationRoles { get; } =
        Array.AsReadOnly(new[] { "Admin", "Organizer", "Customer" });

    // These are identity claims. Missing/malformed values invalidate authentication (401).
    // Role membership is checked separately by authorization policies (403).
    public static bool HasRequiredIdentityClaims(ClaimsPrincipal principal) =>
        ReadRequiredString(principal, Subject, 255) is not null
        && ReadRequiredString(principal, Email, 320) is not null
        && ReadRequiredString(principal, Username, 255) is not null;

    public static string[] ReadApplicationRoles(ClaimsPrincipal principal) => principal.FindAll(Roles)
        .Where(claim => claim.ValueType == ClaimValueTypes.String)
        .Select(claim => claim.Value)
        .Where(role => ApplicationRoles.Contains(role, StringComparer.Ordinal))
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static bool HasSingleApplicationRole(ClaimsPrincipal principal) =>
        ReadApplicationRoles(principal).Length == 1;

    public static IdentityProfile? ReadProfile(ClaimsPrincipal principal)
    {
        if (!HasRequiredIdentityClaims(principal)) return null;
        string[] roles = ReadApplicationRoles(principal);
        if (roles.Length != 1) return null;

        string subject = ReadRequiredString(principal, Subject, 255)!;
        string email = ReadRequiredString(principal, Email, 320)!.Trim();
        string username = ReadRequiredString(principal, Username, 255)!.Trim();
        string name = principal.FindFirstValue("name")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) name = username;

        return new IdentityProfile(subject, email, name[..Math.Min(name.Length, 120)], roles);
    }

    private static string? ReadRequiredString(ClaimsPrincipal principal, string type, int maxLength)
    {
        Claim[] claims = principal.FindAll(type).ToArray();
        return claims.Length == 1 && claims[0].ValueType == ClaimValueTypes.String
            && !string.IsNullOrWhiteSpace(claims[0].Value) && claims[0].Value.Length <= maxLength
            ? claims[0].Value : null;
    }
}
