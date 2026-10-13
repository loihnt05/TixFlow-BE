using TixFlow.Domain.Booking;
using TixFlow.Domain.Common;

namespace TixFlow.Domain.Events;

public sealed class TicketType : Entity
{
    private TicketType()
    {
    }

    public TicketType(
        Guid eventSessionId,
        string name,
        string code,
        decimal price,
        InventoryMode inventoryMode,
        int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price));
        }

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        EventSessionId = eventSessionId;
        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        Price = price;
        InventoryMode = inventoryMode;
        Capacity = capacity;
    }

    public Guid EventSessionId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public decimal Price { get; private set; }

    public string Currency { get; private set; } = "VND";

    public InventoryMode InventoryMode { get; private set; }

    public int Capacity { get; private set; }

    public int MaxPerOrder { get; private set; } = 10;

    public DateTimeOffset? SaleStartsAtUtc { get; private set; }

    public DateTimeOffset? SaleEndsAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public long Version { get; private set; }

    public EventSession EventSession { get; private set; } = null!;

    public TicketInventory Inventory { get; private set; } = null!;

    public ICollection<Seat> Seats { get; private set; } = new List<Seat>();

    public void Update(
        string name,
        string code,
        string? description,
        decimal price,
        int maxPerOrder,
        DateTimeOffset? saleStartsAtUtc,
        DateTimeOffset? saleEndsAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
        if (maxPerOrder <= 0) throw new ArgumentOutOfRangeException(nameof(maxPerOrder));
        if (saleStartsAtUtc.HasValue && saleEndsAtUtc.HasValue && saleEndsAtUtc <= saleStartsAtUtc)
            throw new ArgumentOutOfRangeException(nameof(saleEndsAtUtc), "Sale end must be after sale start.");

        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Price = price;
        MaxPerOrder = maxPerOrder;
        SaleStartsAtUtc = saleStartsAtUtc;
        SaleEndsAtUtc = saleEndsAtUtc;
        Touch();
    }

    public void SetCapacity(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        Touch();
    }

    private void Touch()
    {
        Version++;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
