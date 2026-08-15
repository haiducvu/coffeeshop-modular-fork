using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace CoffeeShop.Api.Health;

public sealed class PostgreSqlReadinessHealthCheck(string connectionString)
    : IHealthCheck
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken)
                .WaitAsync(ConnectionTimeout, cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL readiness check failed.",
                exception);
        }
    }
}
