using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CoffeeShop.Api.Health;

public sealed class RedisReadinessHealthCheck(IConnectionMultiplexer multiplexer)
    : IHealthCheck
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(1);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await multiplexer.GetDatabase()
                .PingAsync()
                .WaitAsync(PingTimeout, cancellationToken);
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Redis readiness check failed.",
                exception);
        }
    }
}
