using TixFlow.Domain.Common;
using TixFlow.Domain.Organizers;

namespace TixFlow.Domain.Events;

public sealed class Venue : Entity
{
    private Venue()
    {
    }

    public Venue(Guid organizerId, string name, string addressLine, string city)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(addressLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);

        OrganizerId = organizerId;
        Name = name.Trim();
        AddressLine = addressLine.Trim();
        City = city.Trim();
    }

    public Guid OrganizerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string AddressLine { get; private set; } = string.Empty;

    public string? Ward { get; private set; }

    public string? District { get; private set; }

    public string City { get; private set; } = string.Empty;

    public string CountryCode { get; private set; } = "VN";

    public string TimeZone { get; private set; } = "Asia/Ho_Chi_Minh";

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public Organizer Organizer { get; private set; } = null!;

    public ICollection<Event> Events { get; private set; } = new List<Event>();

    public void Update(
        string name,
        string addressLine,
        string? ward,
        string? district,
        string city,
        string countryCode,
        string timeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(addressLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);
        if (countryCode.Trim().Length != 2)
            throw new ArgumentOutOfRangeException(nameof(countryCode), "Country code must contain two characters.");

        Name = name.Trim();
        AddressLine = addressLine.Trim();
        Ward = string.IsNullOrWhiteSpace(ward) ? null : ward.Trim();
        District = string.IsNullOrWhiteSpace(district) ? null : district.Trim();
        City = city.Trim();
        CountryCode = countryCode.Trim().ToUpperInvariant();
        TimeZone = timeZone.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
