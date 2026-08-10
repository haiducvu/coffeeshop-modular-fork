using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Counter.Domain.Menu;
using CoffeeShop.SharedKernel.Domain;

namespace CoffeeShop.Modules.Counter.Domain.Orders;

internal sealed class Order : AggregateRoot
{
    private readonly List<LineItem> _lineItems = [];

    private Order()
    {
    }

    private Order(OrderSource source, Location location, Guid loyaltyMemberId)
    {
        Id = Guid.NewGuid();
        Source = source;
        Location = location;
        LoyaltyMemberId = loyaltyMemberId;
        Status = OrderStatus.InProgress;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public OrderSource Source { get; private set; }
    public Location Location { get; private set; }
    public Guid LoyaltyMemberId { get; private set; }
    public OrderStatus Status { get; private set; }
    public Guid Version { get; private set; }
    public IReadOnlyList<LineItem> LineItems => _lineItems;

    public static Order Place(
        OrderSource source,
        Location location,
        Guid loyaltyMemberId,
        IReadOnlyCollection<ItemSelection> selections)
    {
        if (selections.Count == 0)
        {
            throw new DomainException("An order must contain at least one item.");
        }

        var order = new Order(source, location, loyaltyMemberId);
        foreach (var selection in selections)
        {
            var menuItem = MenuCatalog.Get(selection.ItemType);
            if (menuItem.Station != selection.Station)
            {
                throw new DomainException(
                    $"{menuItem.Name} cannot be prepared by {selection.Station}.");
            }

            var lineItem = new LineItem(menuItem);
            order._lineItems.Add(lineItem);
            order.RaiseDomainEvent(new OrderItemAccepted(
                order.Id,
                lineItem.Id,
                lineItem.ItemType,
                lineItem.Station));
        }

        return order;
    }

    public bool CompleteItem(
        Guid lineItemId,
        string madeBy,
        DateTimeOffset occurredAt)
    {
        var lineItem = _lineItems.SingleOrDefault(item => item.Id == lineItemId)
            ?? throw new DomainException(
                $"Line item {lineItemId} does not belong to order {Id}.");

        if (!lineItem.Complete())
        {
            return false;
        }

        Status = _lineItems.All(item => item.Status == ItemStatus.Fulfilled)
            ? OrderStatus.Fulfilled
            : OrderStatus.InProgress;
        Version = Guid.NewGuid();
        RaiseDomainEvent(new OrderUpdated(
            Id,
            lineItem.Id,
            lineItem.ItemType,
            lineItem.Status,
            Status,
            madeBy,
            occurredAt));
        return true;
    }
}
