using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CoffeeShop.Barista.Worker.Telemetry;

internal static class BaristaWorkerOpenTelemetryExtensions
{
    private const string ServiceName = "coffeeshop-barista-worker";

    internal static IServiceCollection AddBaristaWorkerOpenTelemetry(
        this IServiceCollection services,
        BaristaWorkerSettings settings)
    {
        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: ServiceName,
                serviceNamespace: "CoffeeShop",
                serviceVersion: typeof(BaristaWorkerOpenTelemetryExtensions).Assembly
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
