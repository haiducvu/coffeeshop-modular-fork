using CoffeeShop.Messaging.Abstractions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CoffeeShop.Api.Telemetry;

internal static class OpenTelemetryExtensions
{
    private const string ServiceName = "coffeeshop-api";
    private const string CacheMeterName = "CoffeeShop.Fulfillment.Cache";

    internal static IServiceCollection AddCoffeeShopOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint = ResolveOtlpEndpoint(
            configuration["OpenTelemetry:OtlpEndpoint"]);
        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: ServiceName,
                serviceNamespace: "CoffeeShop",
                serviceVersion: typeof(OpenTelemetryExtensions).Assembly
                    .GetName()
                    .Version?
                    .ToString()))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(MessagingTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();
                if (otlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = otlpEndpoint);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(MessagingTelemetry.MeterName, CacheMeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (otlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(options => options.Endpoint = otlpEndpoint);
                }
            });

        return services;
    }

    private static Uri? ResolveOtlpEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || endpoint.AbsolutePath != "/"
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException(
                "OpenTelemetry:OtlpEndpoint must be a canonical absolute HTTP or HTTPS origin.");
        }

        return endpoint;
    }
}
