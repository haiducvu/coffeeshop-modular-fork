using CoffeeShop.IntegrationContracts.Orders;

namespace CoffeeShop.Modules.Barista.Application.Outbox;

internal interface IBaristaOutboxWriter
{
    void Enqueue(
        OrderItemPreparedV1 payload,
        DateTimeOffset occurredAtUtc);
}
