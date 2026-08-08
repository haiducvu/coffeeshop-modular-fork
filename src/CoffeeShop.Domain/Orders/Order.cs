using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;

namespace CoffeeShop.Domain.Orders;

public sealed class Order
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
    }

    public Guid Id { get; private set; }
    public OrderSource Source { get; private set; }
    public Location Location { get; private set; }
    public Guid LoyaltyMemberId { get; private set; }
    public OrderStatus Status { get; private set; }
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
                throw new DomainException($"{menuItem.Name} cannot be prepared by {selection.Station}.");
            }

            order._lineItems.Add(new LineItem(menuItem));
        }

        return order;
    }
}
