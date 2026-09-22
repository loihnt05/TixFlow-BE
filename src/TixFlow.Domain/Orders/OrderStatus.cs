namespace TixFlow.Domain.Orders;

public enum OrderStatus
{
    PendingPayment,
    Paid,
    Cancelled,
    Expired,
    Refunded
}

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Refunded
}

public enum PaymentMethod
{
    Mock,
    Card,
    BankTransfer,
    EWallet
}

public enum TicketStatus
{
    Issued,
    Used,
    Cancelled,
    Refunded
}
