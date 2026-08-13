using CoffeeShop.Modules.Counter.Application.Fulfillment;
using CoffeeShop.Modules.Counter.Infrastructure.Caching;

namespace CoffeeShop.Modules.Counter.Application.Orders.GetFulfilled;

internal sealed class GetFulfilledOrdersHandler(
    IOrderRepository repository,
    IFulfillmentOrdersCache? cache = null,
    FulfillmentCacheMetrics? metrics = null,
    FulfillmentCacheGate? gate = null)
{
    public async Task<IReadOnlyList<FulfilledOrder>> HandleAsync(
        CancellationToken cancellationToken)
    {
        if (cache is null)
        {
            return await GetOrdersFromRepositoryAsync(cancellationToken);
        }

        var cachedOrders = await cache.GetAsync(cancellationToken);
        if (cachedOrders is not null)
        {
            (metrics ?? new FulfillmentCacheMetrics()).RecordHit();
            return cachedOrders;
        }

        (metrics ?? new FulfillmentCacheMetrics()).RecordMiss();
        using var cacheLock = await (gate ?? FulfillmentCacheGate.Default).EnterAsync(cancellationToken);
        cachedOrders = await cache.GetAsync(cancellationToken);
        if (cachedOrders is not null)
        {
            (metrics ?? new FulfillmentCacheMetrics()).RecordHit();
            return cachedOrders;
        }

        var fulfilledOrders = await GetOrdersFromRepositoryAsync(cancellationToken);
        await cache.SetAsync(fulfilledOrders, cancellationToken);
        return fulfilledOrders;
    }

    private async Task<IReadOnlyList<FulfilledOrder>> GetOrdersFromRepositoryAsync(
        CancellationToken cancellationToken)
    {
        var orders = await repository.ListAsync(
            new FulfilledOrdersSpecification(),
            cancellationToken);

        return orders.Select(order => new FulfilledOrder(
            order.Id,
            order.LoyaltyMemberId,
            order.Status.ToString(),
            order.LineItems.Select(lineItem => new FulfilledOrderLineItem(
                lineItem.Id,
                lineItem.Name,
                lineItem.Price,
                lineItem.Station.ToString(),
                lineItem.Status.ToString())).ToArray())).ToArray();
    }
}
