using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
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
            .ValidateOnStart();
        services.AddSingleton<JsonIntegrationEventCodec>();
        services.AddSingleton<KafkaIntegrationEventMapper>();
        services.AddSingleton<IIntegrationEventPublisher, KafkaIntegrationEventPublisher>();
        return services;
    }

    public static IServiceCollection AddKafkaConsumer<TPayload, THandler>(
        this IServiceCollection services,
        string consumerRole)
        where TPayload : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TPayload>
    {
        if (!IsValidName(consumerRole))
        {
            throw new ArgumentException(
                "Kafka consumer role must contain only letters, digits, periods, underscores, or hyphens.",
                nameof(consumerRole));
        }

        services.AddSingleton<IHostedService>(serviceProvider =>
            ActivatorUtilities.CreateInstance<KafkaConsumerWorker<TPayload, THandler>>(
                serviceProvider,
                consumerRole));
        return services;
    }

    private static bool IsValidName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or '-');
}
