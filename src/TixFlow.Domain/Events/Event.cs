using TixFlow.Domain.Common;
using TixFlow.Domain.Organizers;

namespace TixFlow.Domain.Events;

public sealed class Event : Entity
{
    private Event()
    {
    }

    public Event(Guid organizerId, string name, string slug, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        OrganizerId = organizerId;
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Category = category.Trim();
    }

    public Guid OrganizerId { get; private set; }

    public Guid? VenueId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string Category { get; private set; } = string.Empty;

    public string? ImageUrl { get; private set; }

    public EventStatus Status { get; private set; } = EventStatus.Draft;

    public DateTimeOffset? SaleStartsAtUtc { get; private set; }

    public DateTimeOffset? SaleEndsAtUtc { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public Organizer Organizer { get; private set; } = null!;

    public Venue? Venue { get; private set; }

    public ICollection<EventSession> Sessions { get; private set; } = new List<EventSession>();
}
