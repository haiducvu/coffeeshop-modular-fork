using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.SharedKernel.Domain;

namespace CoffeeShop.Modules.Barista.Domain;

internal sealed class BaristaItem : AggregateRoot
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
        DateTimeOffset timeIn) =>
        new(orderId, lineItemId, itemType, ItemNameFor(itemType), timeIn);

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

    private static string ItemNameFor(ItemType itemType) => itemType switch
    {
        ItemType.Cappuccino => "CAPPUCCINO",
        ItemType.CoffeeBlack => "COFFEE_BLACK",
        ItemType.CoffeeWithRoom => "COFFEE_WITH_ROOM",
        ItemType.Espresso => "ESPRESSO",
        ItemType.EspressoDouble => "ESPRESSO_DOUBLE",
        ItemType.Latte => "LATTE",
        _ => throw new ArgumentOutOfRangeException(nameof(itemType), itemType, "Not a barista item.")
    };
}
