using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CoffeeShop.Kitchen.Worker.Telemetry;

internal static class KitchenWorkerOpenTelemetryExtensions
{
    private const string ServiceName = "coffeeshop-kitchen-worker";

    internal static IServiceCollection AddKitchenWorkerOpenTelemetry(
        this IServiceCollection services,
        KitchenWorkerSettings settings)
    {
        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: ServiceName,
                serviceNamespace: "CoffeeShop",
                serviceVersion: typeof(KitchenWorkerOpenTelemetryExtensions).Assembly
                    .GetName()
                    .Version?
                    .ToString()))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(MessagingTelemetry.ActivitySourceName)
                    .AddEntityFrameworkCoreInstrumentation();
                if (settings.OtlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter(options =>
                        options.Endpoint = settings.OtlpEndpoint);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(MessagingTelemetry.MeterName)
                    .AddRuntimeInstrumentation();
                if (settings.OtlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(options =>
                        options.Endpoint = settings.OtlpEndpoint);
                }
            });

        return services;
    }
}
