namespace TixFlow.Domain.Booking;

public enum HoldStatus
{
    Active,
    Confirmed,
    Expired,
    Cancelled
}

public enum SeatAllocationStatus
{
    Held,
    Sold,
    Released
}
