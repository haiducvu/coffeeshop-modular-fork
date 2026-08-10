using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.SharedKernel.Domain;

namespace CoffeeShop.Modules.Kitchen.Domain;

internal sealed class KitchenItem : AggregateRoot
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
        DateTimeOffset timeIn) =>
        new(orderId, lineItemId, itemType, ItemNameFor(itemType), timeIn);

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

    private static string ItemNameFor(ItemType itemType) => itemType switch
    {
        ItemType.CakePop => "CAKEPOP",
        ItemType.Croissant => "CROISSANT",
        ItemType.Muffin => "MUFFIN",
        ItemType.CroissantChocolate => "CROISSANT_CHOCOLATE",
        _ => throw new ArgumentOutOfRangeException(nameof(itemType), itemType, "Not a kitchen item.")
    };
}
