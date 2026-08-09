using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders.Events;

namespace CoffeeShop.Domain.Barista;

public sealed class BaristaItem : AggregateRoot
{
    private BaristaItem()
    {
    }

    private BaristaItem(
        Guid orderId,
        Guid lineItemId,
        ItemType itemType,
        string itemName,
        DateTimeOffset timeIn)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        LineItemId = lineItemId;
        ItemType = itemType;
        ItemName = itemName;
        TimeIn = timeIn;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid LineItemId { get; private set; }
    public ItemType ItemType { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public DateTimeOffset TimeIn { get; private set; }
    public DateTimeOffset? TimeUp { get; private set; }

    public static BaristaItem Accept(
        Guid orderId,
        Guid lineItemId,
        ItemType itemType,
        DateTimeOffset timeIn)
    {
        var menuItem = MenuCatalog.Get(itemType);
        return new BaristaItem(orderId, lineItemId, itemType, menuItem.Name, timeIn);
    }

    public void Complete(DateTimeOffset timeUp)
    {
        TimeUp = timeUp;
        RaiseDomainEvent(new OrderItemPrepared(
            OrderId,
            LineItemId,
            ItemType,
            PreparationStation.Barista,
            "barista",
            timeUp));
    }
}
