using CoffeeShop.Messaging.Dapr;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Api.Health;

public sealed class DaprReadinessHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<DaprMessagingOptions> options) : IHealthCheck
{
    public const string HttpClientName = "dapr-readiness";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{options.Value.SidecarHttpEndpoint.TrimEnd('/')}/v1.0/healthz");
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Dapr sidecar is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"Dapr sidecar returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Dapr sidecar readiness check failed.",
                exception);
        }
    }
}
