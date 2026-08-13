namespace CoffeeShop.Modules.Counter.Application.Fulfillment;

internal sealed class FulfillmentCacheGate
{
    private static readonly FulfillmentCacheGate ProcessWide = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public static FulfillmentCacheGate Default => ProcessWide;

    public async ValueTask<Releaser> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    internal readonly struct Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
