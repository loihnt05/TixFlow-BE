using TixFlow.Domain.Common;
using TixFlow.Domain.Identity;

namespace TixFlow.Domain.Organizers;

public sealed class Organizer : Entity
{
    private Organizer()
    {
    }

    public Organizer(Guid ownerUserId, string name, string slug, string contactEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(contactEmail);

        OwnerUserId = ownerUserId;
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        ContactEmail = contactEmail.Trim();
    }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string ContactEmail { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public User OwnerUser { get; private set; } = null!;
}
