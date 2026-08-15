using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CoffeeShop.Api.Health;

public sealed class IdentityProviderReadinessHealthCheck(
    IHttpClientFactory httpClientFactory,
    Uri discoveryEndpoint) : IHealthCheck
{
    public const string HttpClientName = "identity-provider-readiness";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, discoveryEndpoint);
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Identity discovery is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"Identity discovery returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Identity discovery readiness check failed.",
                exception);
        }
    }
}
