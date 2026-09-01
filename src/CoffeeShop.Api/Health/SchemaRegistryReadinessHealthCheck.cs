using CoffeeShop.Messaging.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Api.Health;

public sealed class SchemaRegistryReadinessHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<KafkaMessagingOptions> options) : IHealthCheck
{
    public const string HttpClientName = "schema-registry-readiness";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{options.Value.SchemaRegistryUrl.TrimEnd('/')}/subjects");
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Schema Registry is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"Schema Registry returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Schema Registry readiness check failed.",
                exception);
        }
    }
}
