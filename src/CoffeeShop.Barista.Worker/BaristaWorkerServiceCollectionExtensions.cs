using CoffeeShop.Barista.Worker.Events;
using CoffeeShop.Barista.Worker.Telemetry;
using CoffeeShop.Barista.Worker.Time;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Barista.Infrastructure.Outbox;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CoffeeShop.Barista.Worker;

public static class BaristaWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddBaristaWorker(
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
        services.AddBaristaModule(
            settings.PostgreSqlConnectionString,
            configuration.GetSection(BaristaOutboxOptions.SectionName).Bind);
        services.AddKafkaConsumer<OrderPlacedV1>("barista");
        services.AddBaristaWorkerOpenTelemetry(settings);
        return services;
    }

    public static void ValidateBaristaWorkerOptions(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.GetRequiredService<IOptions<KafkaMessagingOptions>>().Value;
        _ = services.GetRequiredService<IOptions<BaristaOutboxOptions>>().Value;
    }

    private static BaristaWorkerSettings ReadSettings(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Barista");
        var failures = new List<string>();
        if (!IsValidPostgreSqlConnectionString(connectionString))
        {
            failures.Add(
                "ConnectionStrings:Barista is required and must be a valid PostgreSQL connection string.");
        }

        var otlpEndpoint = ResolveOtlpEndpoint(
            configuration["OpenTelemetry:OtlpEndpoint"],
            failures);
        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(BaristaWorkerSettings),
                typeof(BaristaWorkerSettings),
                failures);
        }

        return new BaristaWorkerSettings(connectionString!, otlpEndpoint);
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
