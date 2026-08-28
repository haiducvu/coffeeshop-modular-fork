using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka.Retry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CoffeeShop.Messaging.Kafka;

public static class KafkaServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaMessaging(
        this IServiceCollection services,
        Action<KafkaMessagingOptions> configure)
    {
        services.AddOptions<KafkaMessagingOptions>()
            .Configure(configure)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.BootstrapServers),
                "Kafka bootstrap servers are required.")
            .Validate(
                options => IsValidName(options.TopicPrefix),
                "Kafka topic prefix must contain only letters, digits, periods, underscores, or hyphens.")
            .Validate(
                options => IsValidName(options.ConsumerGroupPrefix),
                "Kafka consumer group prefix must contain only letters, digits, periods, underscores, or hyphens.")
            .Validate(
                options => options.Retry is not null
                    && options.Retry.FirstDelay > TimeSpan.Zero
                    && options.Retry.SecondDelay > options.Retry.FirstDelay,
                "Kafka retry delays must be positive and the second delay must be greater than the first.")
            .Validate(
                options => options.Retry is not null
                    && options.Retry.MaxPollInterval.TotalMilliseconds <= int.MaxValue
                    && options.Retry.MaxPollInterval
                        >= KafkaRetryOptions.MinimumMaxPollInterval
                    && options.Retry.MaxPollInterval - options.Retry.SecondDelay
                        >= TimeSpan.FromSeconds(1),
                "Kafka max poll interval must be at least five minutes, exceed the second retry delay by at least one second, and fit the Kafka client range.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<
            IIntegrationFailureClassifier,
            DefaultIntegrationFailureClassifier>();
        services.TryAddSingleton<IRetryDelay, TimeProviderRetryDelay>();
        services.TryAddSingleton<IKafkaRetryPublisher, KafkaRetryPublisher>();
        services.TryAddSingleton<KafkaRetryRouter>();
        services.AddSingleton<JsonIntegrationEventCodec>();
        services.AddSingleton<KafkaIntegrationEventMapper>();
        services.AddSingleton<IIntegrationEventPublisher, KafkaIntegrationEventPublisher>();
        return services;
    }

    public static IServiceCollection AddKafkaConsumer<TPayload>(
        this IServiceCollection services,
        string consumerRole)
        where TPayload : IIntegrationEvent
    {
        if (!IsValidName(consumerRole))
        {
            throw new ArgumentException(
                "Kafka consumer role must contain only letters, digits, periods, underscores, or hyphens.",
                nameof(consumerRole));
        }

        foreach (var stage in Enum.GetValues<KafkaConsumerStage>())
        {
            services.AddSingleton<IHostedService>(serviceProvider =>
                ActivatorUtilities.CreateInstance<KafkaConsumerWorker<TPayload>>(
                    serviceProvider,
                    consumerRole,
                    stage));
        }

        return services;
    }

    public static IServiceCollection AddKafkaConsumer<TPayload, THandler>(
        this IServiceCollection services,
        string consumerRole)
        where TPayload : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TPayload>
    {
        services.AddKeyedScoped<IIntegrationEventHandler<TPayload>>(
            consumerRole,
            (serviceProvider, _) => serviceProvider.GetRequiredService<THandler>());
        return services.AddKafkaConsumer<TPayload>(consumerRole);
    }

    private static bool IsValidName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or '-');
}
