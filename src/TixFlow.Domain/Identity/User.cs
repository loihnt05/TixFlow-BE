using TixFlow.Domain.Common;

namespace TixFlow.Domain.Identity;

public sealed class User : Entity
{
    private User()
    {
    }

    public User(string email, string displayName, string passwordHash, UserRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        Email = email.Trim();
        NormalizedEmail = email.Trim().ToUpperInvariant();
        DisplayName = displayName.Trim();
        PasswordHash = passwordHash;
        Role = role;
    }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; } = UserRole.Customer;

    public UserStatus Status { get; private set; } = UserStatus.Active;

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
}

public enum UserRole
{
    Admin,
    Organizer,
    Customer
}

public enum UserStatus
{
    Active,
    Suspended,
    Disabled
}
