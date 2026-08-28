using CoffeeShop.IntegrationContracts.Orders;

namespace CoffeeShop.Modules.Kitchen.Application.Outbox;

internal interface IKitchenOutboxWriter
{
    void Enqueue(
        OrderItemPreparedV1 payload,
        DateTimeOffset occurredAtUtc);
}
