namespace TixFlow.Domain.Events;

public enum EventStatus
{
    Draft,
    Published,
    Cancelled,
    Completed
}

public enum SessionStatus
{
    Scheduled,
    OnSale,
    SoldOut,
    Cancelled,
    Completed
}

public enum InventoryMode
{
    ReservedSeating,
    GeneralAdmission
}
