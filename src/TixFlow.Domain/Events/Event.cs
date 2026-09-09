using TixFlow.Domain.Common;

namespace TixFlow.Domain.Events;

public sealed class Event : Entity
{
    private Event()
    {
    }

    public Event(string name, DateTimeOffset startsAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        StartsAtUtc = startsAtUtc;
    }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset StartsAtUtc { get; private set; }
}

