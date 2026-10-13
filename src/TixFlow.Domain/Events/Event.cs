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

    public long Version { get; private set; }

    public Organizer Organizer { get; private set; } = null!;

    public Venue? Venue { get; private set; }

    public ICollection<EventSession> Sessions { get; private set; } = new List<EventSession>();

    public void Update(
        string name,
        string slug,
        string description,
        string category,
        Guid? venueId,
        string? imageUrl,
        DateTimeOffset? saleStartsAtUtc,
        DateTimeOffset? saleEndsAtUtc)
    {
        if (Status != EventStatus.Draft)
            throw new InvalidOperationException("Only draft events can be edited.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ValidateSaleWindow(saleStartsAtUtc, saleEndsAtUtc);
        ValidateImageUrl(imageUrl);

        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = description.Trim();
        Category = category.Trim();
        VenueId = venueId;
        ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        SaleStartsAtUtc = saleStartsAtUtc;
        SaleEndsAtUtc = saleEndsAtUtc;
        Touch();
    }

    public void Publish(DateTimeOffset publishedAtUtc)
    {
        if (Status != EventStatus.Draft)
            throw new InvalidOperationException("Only draft events can be published.");
        if (Sessions.Count == 0)
            throw new InvalidOperationException("An event needs at least one session before publishing.");
        if (Sessions.Any(session => session.EndsAtUtc <= publishedAtUtc))
            throw new InvalidOperationException("All sessions must end after the publish time.");

        Status = EventStatus.Published;
        PublishedAtUtc = publishedAtUtc;
        Touch();
    }

    public void Cancel()
    {
        if (Status is not (EventStatus.Draft or EventStatus.Published))
            throw new InvalidOperationException("Only draft or published events can be cancelled.");
        Status = EventStatus.Cancelled;
        Touch();
    }

    public void Complete(DateTimeOffset completedAtUtc)
    {
        if (Status != EventStatus.Published)
            throw new InvalidOperationException("Only published events can be completed.");
        if (Sessions.Count == 0 || Sessions.Any(session => session.EndsAtUtc > completedAtUtc))
            throw new InvalidOperationException("All sessions must have ended before completing the event.");
        Status = EventStatus.Completed;
        Touch();
    }

    private static void ValidateSaleWindow(DateTimeOffset? startsAtUtc, DateTimeOffset? endsAtUtc)
    {
        if (startsAtUtc.HasValue && endsAtUtc.HasValue && endsAtUtc <= startsAtUtc)
            throw new ArgumentOutOfRangeException(nameof(endsAtUtc), "Sale end must be after sale start.");
    }

    private static void ValidateImageUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return;
        if (!Uri.TryCreate(imageUrl.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Image URL must be an absolute HTTP or HTTPS URL.", nameof(imageUrl));
    }

    private void Touch()
    {
        Version++;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
