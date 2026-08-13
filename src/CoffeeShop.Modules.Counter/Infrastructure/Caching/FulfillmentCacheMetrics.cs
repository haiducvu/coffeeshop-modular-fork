using System.Diagnostics.Metrics;

namespace CoffeeShop.Modules.Counter.Infrastructure.Caching;

internal sealed class FulfillmentCacheMetrics
{
    private static readonly Meter Meter = new("CoffeeShop.Fulfillment.Cache");
    private static readonly Counter<long> Hit = Meter.CreateCounter<long>(
        "coffeeshop.fulfillment.cache.hit");
    private static readonly Counter<long> Miss = Meter.CreateCounter<long>(
        "coffeeshop.fulfillment.cache.miss");
    private static readonly Counter<long> Invalidation = Meter.CreateCounter<long>(
        "coffeeshop.fulfillment.cache.invalidation");

    public void RecordHit() => Hit.Add(1);

    public void RecordMiss() => Miss.Add(1);

    public void RecordInvalidation() => Invalidation.Add(1);
}
