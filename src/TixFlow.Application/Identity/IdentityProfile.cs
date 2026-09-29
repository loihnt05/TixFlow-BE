using TixFlow.Domain.Identity;

namespace TixFlow.Application.Identity;

public sealed record IdentityProfile(string Subject, string Email, string Name, string[] Roles)
{
    // The legacy single role is a profile snapshot, never an authorization source.
    public UserRole PrimaryRole => Roles.Contains(nameof(UserRole.Admin), StringComparer.Ordinal)
        ? UserRole.Admin
        : Roles.Contains(nameof(UserRole.Organizer), StringComparer.Ordinal)
            ? UserRole.Organizer
            : UserRole.Customer;
}

public interface ILocalUserSynchronizer
{
    Task<User> SynchronizeAsync(IdentityProfile profile, CancellationToken cancellationToken);
}

public sealed class ProfileConflictException : Exception
{
    public ProfileConflictException(Exception innerException)
        : base("This email belongs to another local profile. An administrator must review account linking.", innerException)
    {
    }
}
