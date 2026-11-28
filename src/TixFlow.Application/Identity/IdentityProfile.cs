using System.Text.RegularExpressions;
using TixFlow.Domain.Identity;

namespace TixFlow.Application.Identity;

public sealed record IdentityProfile(string Subject, string Email, string Name, string[] Roles, string? PhoneNumber = null)
{
    // The legacy single role is a profile snapshot, never an authorization source.
    public UserRole PrimaryRole => Roles switch
    {
        [nameof(UserRole.Admin)] => UserRole.Admin,
        [nameof(UserRole.Organizer)] => UserRole.Organizer,
        [nameof(UserRole.Customer)] => UserRole.Customer,
        _ => throw new InvalidOperationException("An identity profile must have exactly one business role.")
    };
}

public interface ILocalUserSynchronizer
{
    Task<User> SynchronizeAsync(IdentityProfile profile, CancellationToken cancellationToken);
}

public static partial class PhoneNumbers
{
    public const int MaxLength = 32;

    /// <summary>
    /// Trims and collapses whitespace. Returns null for blank input or when the value is not a
    /// plausible phone number (optional leading '+', digits and common separators, 6–15 digits).
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string collapsed = Whitespace().Replace(value.Trim(), " ");
        if (collapsed.Length > MaxLength || !Allowed().IsMatch(collapsed)) return null;
        int digits = collapsed.Count(char.IsAsciiDigit);
        return digits is >= 6 and <= 15 ? collapsed : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\+?[0-9 ().\-]+$")]
    private static partial Regex Allowed();
}

public sealed class ProfileConflictException : Exception
{
    public ProfileConflictException(Exception innerException)
        : base("This email belongs to another local profile. An administrator must review account linking.", innerException)
    {
    }
}
