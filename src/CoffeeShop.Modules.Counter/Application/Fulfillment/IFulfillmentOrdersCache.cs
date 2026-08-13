namespace CoffeeShop.Modules.Counter.Application.Fulfillment;

internal interface IFulfillmentOrdersCache
{
    Task<IReadOnlyList<FulfilledOrder>?> GetAsync(CancellationToken cancellationToken);

    Task SetAsync(IReadOnlyList<FulfilledOrder> orders, CancellationToken cancellationToken);

    Task RemoveAsync(CancellationToken cancellationToken);
}
