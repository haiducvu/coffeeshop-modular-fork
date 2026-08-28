using Confluent.Kafka;

namespace CoffeeShop.Messaging.Kafka;

internal static class KafkaClientConfigFactory
{
    internal const int SessionTimeoutMilliseconds = 10_000;

    internal static ProducerConfig CreateProducer(KafkaMessagingOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true
    };

    internal static ConsumerConfig CreateConsumer(
        KafkaMessagingOptions options,
        string consumerRole) => new()
    {
        BootstrapServers = options.BootstrapServers,
        GroupId = $"{options.ConsumerGroupPrefix}.{consumerRole}",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
        TopicMetadataRefreshIntervalMs = 1_000,
        SessionTimeoutMs = SessionTimeoutMilliseconds,
        MaxPollIntervalMs = checked(
            (int)options.Retry.MaxPollInterval.TotalMilliseconds)
    };
}
