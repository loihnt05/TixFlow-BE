namespace TixFlow.Api.Modules.Booking;

public sealed class BookingOptions
{
    public const string SectionName = "Booking";

    public int HoldDurationMinutes { get; init; } = 10;

    public int LockTimeoutSeconds { get; init; } = 5;

    public int IdempotencyRetentionHours { get; init; } = 24;
}
