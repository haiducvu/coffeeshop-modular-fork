using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders.Events;

namespace CoffeeShop.Domain.Kitchen;

public sealed class KitchenItem : AggregateRoot
{
    private KitchenItem()
    {
    }

    private KitchenItem(
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

    public static KitchenItem Accept(
        Guid orderId,
        Guid lineItemId,
        ItemType itemType,
        DateTimeOffset timeIn)
    {
        var menuItem = MenuCatalog.Get(itemType);
        return new KitchenItem(orderId, lineItemId, itemType, menuItem.Name, timeIn);
    }

    public void Complete(DateTimeOffset timeUp)
    {
        TimeUp = timeUp;
        RaiseDomainEvent(new OrderItemPrepared(
            OrderId,
            LineItemId,
            ItemType,
            PreparationStation.Kitchen,
            "kitchen",
            timeUp));
    }
}
