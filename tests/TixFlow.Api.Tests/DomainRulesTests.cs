using TixFlow.Domain.Booking;
using TixFlow.Domain.Events;
using TixFlow.Domain.Orders;

namespace TixFlow.Api.Tests;

[Trait("Category", "Unit")]
public sealed class DomainRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);
    private static Hold Hold() => new(Guid.NewGuid(), Guid.NewGuid(), "key", 500000m, Start, Start.AddMinutes(10));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void ExpiryUsesAnInclusiveDeadline(int ticks, bool mayExpire)
    {
        var hold = Hold();
        var decision = hold.ExpiresAtUtc.AddTicks(ticks);
        if (mayExpire)
        {
            hold.Expire(decision);
            Assert.Equal(HoldStatus.Expired, hold.Status);
            Assert.Equal(1, hold.Version);
            Assert.Equal(decision, hold.UpdatedAtUtc);
            Assert.Throws<InvalidOperationException>(() => hold.Cancel(decision));
            Assert.Throws<InvalidOperationException>(() => hold.Expire(decision));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => hold.Expire(decision));
            Assert.Equal(HoldStatus.Active, hold.Status);
            Assert.Equal(0, hold.Version);
        }
    }

    [Fact]
    public void CancellationIsIdempotentAndTerminal()
    {
        var hold = Hold();
        hold.Cancel(Start.AddMinutes(1));
        hold.Cancel(Start.AddMinutes(2));
        Assert.Equal(HoldStatus.Cancelled, hold.Status);
        Assert.Equal(1, hold.Version);
        Assert.Equal(Start.AddMinutes(1), hold.UpdatedAtUtc);
        Assert.Throws<InvalidOperationException>(() => hold.Expire(Start.AddHours(1)));
    }

    [Fact]
    public void PaidOrderCannotBePaidTwice()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " order-1 ", 500000m);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        order.MarkPaid(Start);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(1, order.Version);
        Assert.Equal(Start, order.UpdatedAtUtc);
        Assert.Throws<InvalidOperationException>(() => order.MarkPaid(Start.AddSeconds(1)));
        Assert.Equal(1, order.Version);
    }

    [Fact]
    public void SessionTransitionsAndCompletionBoundaryAreGuarded()
    {
        var session = new EventSession(Guid.NewGuid(), "Session", Start, Start.AddHours(2));
        Assert.Throws<InvalidOperationException>(() => session.SetStatus(SessionStatus.Completed, Start));
        session.SetStatus(SessionStatus.OnSale, Start);
        session.SetStatus(SessionStatus.SoldOut, Start);
        session.SetStatus(SessionStatus.OnSale, Start);
        Assert.Throws<InvalidOperationException>(() => session.SetStatus(SessionStatus.Completed, session.EndsAtUtc.AddTicks(-1)));
        session.SetStatus(SessionStatus.Completed, session.EndsAtUtc);
        Assert.Throws<InvalidOperationException>(() => session.SetStatus(SessionStatus.OnSale, session.EndsAtUtc));
        Assert.Throws<InvalidOperationException>(() => session.Update("Changed", Start, Start.AddHours(3)));
        Assert.Equal(4, session.Version);
    }

    [Fact]
    public void CancelledEventCannotPublishOrComplete()
    {
        var item = new Event(Guid.NewGuid(), "Event", "slug", "Music");
        Assert.Throws<InvalidOperationException>(() => item.Publish(Start));
        item.Cancel();
        Assert.Throws<InvalidOperationException>(() => item.Publish(Start));
        Assert.Throws<InvalidOperationException>(() => item.Cancel());
        Assert.Throws<InvalidOperationException>(() => item.Complete(Start));
    }

    [Theory]
    [InlineData(0, 10, 2, 0)]
    [InlineData(-1, 10, 2, 0)]
    [InlineData(1, -1, 2, 0)]
    [InlineData(3, 10, 2, 0)]
    [InlineData(1, 10, 0, 0)]
    [InlineData(1, 10, 2, -1)]
    public void InvalidHoldItemCannotEnterTheDomain(int quantity, decimal price, int limit, decimal fee)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new HoldItem(Guid.NewGuid(), Guid.NewGuid(), quantity, price, limit, "Ticket", fee));

    [Fact]
    public void PriceSnapshotsSurviveCatalogEditsIncludingFreeTicketsAndDecimalFees()
    {
        var type = new TicketType(Guid.NewGuid(), "Original", "T", 123456.78m, InventoryMode.GeneralAdmission, 50);
        var held = new HoldItem(Guid.NewGuid(), type.Id, 2, type.Price, 2, type.Name, 100.25m);
        var item = new OrderItem(Guid.NewGuid(), type.Id, held.Quantity, held.UnitPrice, held.TicketTypeNameSnapshot, held.ServiceFee);
        type.Update("Changed", "T", null, 0, 1, null, null);
        Assert.Equal(247013.81m, item.UnitPrice * item.Quantity + item.ServiceFee);
        Assert.Equal("Original", item.TicketTypeNameSnapshot);
        Assert.Equal(2, held.MaxPerOrderSnapshot);
        Assert.Equal(0, new HoldItem(Guid.NewGuid(), type.Id, 1, type.Price, 1, type.Name).UnitPrice);
    }

    [Fact]
    public void InvalidDatesMoneyCapacityAndImageUrlAreRejectedWithoutMutation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Hold(Guid.NewGuid(), Guid.NewGuid(), "k", 0, Start, Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Order(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "o", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TicketInventory(Guid.NewGuid(), 0));
        var type = new TicketType(Guid.NewGuid(), "Original", "T", 10, InventoryMode.GeneralAdmission, 50);
        Assert.Throws<ArgumentOutOfRangeException>(() => type.Update("Changed", "T", null, 10, 2, Start, Start));
        Assert.Equal("Original", type.Name);
        Assert.Equal(0, type.Version);
        var item = new Event(Guid.NewGuid(), "Original", "slug", "Music");
        Assert.Throws<ArgumentException>(() => item.Update("Changed", "slug", "", "Music", null, "javascript:alert(1)", null, null));
        Assert.Equal("Original", item.Name);
    }
}
