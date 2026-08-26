using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Kafka;

public sealed class KafkaReadinessHealthCheck(
    IOptions<KafkaMessagingOptions> options) : IHealthCheck
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(1);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = options.Value.BootstrapServers
            }).Build();
            var metadata = admin.GetMetadata(MetadataTimeout);
            return Task.FromResult(metadata.Brokers.Count > 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Kafka broker is unavailable."));
        }
        catch (KafkaException)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Kafka broker is unavailable."));
        }
    }
}
