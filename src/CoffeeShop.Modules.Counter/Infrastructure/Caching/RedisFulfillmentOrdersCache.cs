using System.Text.Json;
using CoffeeShop.Modules.Counter.Application.Fulfillment;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace CoffeeShop.Modules.Counter.Infrastructure.Caching;

internal sealed class RedisFulfillmentOrdersCache(
    IDistributedCache cache,
    FulfillmentCacheOptions options,
    ILogger<RedisFulfillmentOrdersCache> logger) : IFulfillmentOrdersCache
{
    private const string CacheKey = "fulfilled-orders:v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        WriteIndented = false
    };

    public async Task<IReadOnlyList<FulfilledOrder>?> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var timeout = CreateTimeout(cancellationToken);
            var payload = await cache.GetAsync(CacheKey, timeout.Token);
            if (payload is null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<FulfilledOrder[]>(payload, JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Fulfillment cache returned malformed data.");
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Fulfillment cache command timed out.");
            return null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Fulfillment cache read failed.");
            return null;
        }
    }

    public async Task SetAsync(
        IReadOnlyList<FulfilledOrder> orders,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(orders, JsonOptions);
            using var timeout = CreateTimeout(cancellationToken);
            await cache.SetAsync(
                CacheKey,
                payload,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = options.TimeToLive
                },
                timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Fulfillment cache command timed out.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Fulfillment cache write failed.");
        }
    }

    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var timeout = CreateTimeout(cancellationToken);
            await cache.RemoveAsync(CacheKey, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Fulfillment cache command timed out.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Fulfillment cache invalidation failed.");
        }
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FulfillmentCacheOptions.CommandTimeout);
        return timeout;
    }
}
