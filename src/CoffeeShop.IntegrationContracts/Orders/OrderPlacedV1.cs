using System.Text.Json.Serialization;

namespace CoffeeShop.IntegrationContracts.Orders;

public sealed record OrderPlacedV1(
    [property: JsonRequired] Guid OrderId,
    [property: JsonRequired] IReadOnlyList<OrderLineItemV1> Items)
    : IIntegrationEvent
{
    public static string EventType => "coffeeshop.order-placed";
    public static int EventVersion => 1;
}

public sealed record OrderLineItemV1(
    [property: JsonRequired] Guid LineItemId,
    [property: JsonRequired] string ItemType,
    [property: JsonRequired] string Station);
