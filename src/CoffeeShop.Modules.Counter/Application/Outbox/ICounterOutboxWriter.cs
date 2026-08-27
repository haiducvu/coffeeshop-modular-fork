using CoffeeShop.IntegrationContracts.Orders;

namespace CoffeeShop.Modules.Counter.Application.Outbox;

internal interface ICounterOutboxWriter
{
    void Enqueue(OrderPlacedV1 payload, DateTimeOffset occurredAtUtc);
}
