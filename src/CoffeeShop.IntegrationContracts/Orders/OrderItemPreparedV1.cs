using System.Text.Json.Serialization;

namespace CoffeeShop.IntegrationContracts.Orders;

public sealed record OrderItemPreparedV1(
    [property: JsonRequired] Guid OrderId,
    [property: JsonRequired] Guid LineItemId,
    [property: JsonRequired] string ItemType,
    [property: JsonRequired] string Station,
    [property: JsonRequired] string MadeBy,
    [property: JsonRequired] DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent
{
    public static string EventType => "coffeeshop.order-item-prepared";
    public static int EventVersion => 1;
}
