using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Kitchen.Worker.Events;
using CoffeeShop.Kitchen.Worker.Telemetry;
using CoffeeShop.Kitchen.Worker.Time;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Modules.Kitchen;
using CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CoffeeShop.Kitchen.Worker;

public static class KitchenWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddKitchenWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = ReadSettings(configuration);
        services.AddSingleton(settings);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPreparationDelay, TaskPreparationDelay>();
        services.TryAddScoped<
            IDomainEventDispatcher,
            ServiceProviderDomainEventDispatcher>();

        var kafkaSection = configuration.GetSection(KafkaMessagingOptions.SectionName);
        services.AddKafkaMessaging(kafkaSection.Bind);
        services.AddKitchenModule(
            settings.PostgreSqlConnectionString,
            configuration.GetSection(KitchenOutboxOptions.SectionName).Bind);
        services.AddKafkaConsumer<OrderPlacedV1>("kitchen");
        services.AddKitchenWorkerOpenTelemetry(settings);
        return services;
    }

    public static void ValidateKitchenWorkerOptions(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.GetRequiredService<IOptions<KafkaMessagingOptions>>().Value;
        _ = services.GetRequiredService<IOptions<KitchenOutboxOptions>>().Value;
    }

    private static KitchenWorkerSettings ReadSettings(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Kitchen");
        var failures = new List<string>();
        if (!IsValidPostgreSqlConnectionString(connectionString))
        {
            failures.Add(
                "ConnectionStrings:Kitchen is required and must be a valid PostgreSQL connection string.");
        }

        var otlpEndpoint = ResolveOtlpEndpoint(
            configuration["OpenTelemetry:OtlpEndpoint"],
            failures);
        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(KitchenWorkerSettings),
                typeof(KitchenWorkerSettings),
                failures);
        }

        return new KitchenWorkerSettings(connectionString!, otlpEndpoint);
    }

    private static bool IsValidPostgreSqlConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(builder.Host)
                && !string.IsNullOrWhiteSpace(builder.Database)
                && !string.IsNullOrWhiteSpace(builder.Username);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return false;
        }
    }

    private static Uri? ResolveOtlpEndpoint(
        string? value,
        ICollection<string> failures)
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
            failures.Add(
                "OpenTelemetry:OtlpEndpoint must be a canonical absolute HTTP or HTTPS origin.");
            return null;
        }

        return endpoint;
    }
}
