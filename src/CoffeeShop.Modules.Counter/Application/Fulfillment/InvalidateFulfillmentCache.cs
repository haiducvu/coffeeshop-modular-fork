using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Counter.Infrastructure.Caching;
using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.Modules.Counter.Application.Fulfillment;

internal sealed class InvalidateFulfillmentCache(
    IFulfillmentOrdersCache? cache = null,
    FulfillmentCacheMetrics? metrics = null,
    FulfillmentCacheGate? gate = null) : IDomainEventHandler<OrderUpdated>
{
    public async Task HandleAsync(OrderUpdated updated, CancellationToken cancellationToken)
    {
        if (updated.OrderStatus != OrderStatus.Fulfilled || cache is null)
        {
            return;
        }

        using var cacheLock = await (gate ?? FulfillmentCacheGate.Default).EnterAsync(cancellationToken);
        await cache.RemoveAsync(cancellationToken);
        (metrics ?? new FulfillmentCacheMetrics()).RecordInvalidation();
    }
}
