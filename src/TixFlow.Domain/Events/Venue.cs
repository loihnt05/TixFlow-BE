using TixFlow.Domain.Common;

namespace TixFlow.Domain.Events;

public sealed class Venue : Entity
{
    private Venue()
    {
    }

    public Venue(string name, string addressLine, string city)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(addressLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);

        Name = name.Trim();
        AddressLine = addressLine.Trim();
        City = city.Trim();
    }

    public string Name { get; private set; } = string.Empty;

    public string AddressLine { get; private set; } = string.Empty;

    public string? Ward { get; private set; }

    public string? District { get; private set; }

    public string City { get; private set; } = string.Empty;

    public string CountryCode { get; private set; } = "VN";

    public string TimeZone { get; private set; } = "Asia/Ho_Chi_Minh";

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
}
