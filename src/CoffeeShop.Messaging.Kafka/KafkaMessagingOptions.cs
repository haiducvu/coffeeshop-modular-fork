namespace CoffeeShop.Messaging.Kafka;

using CoffeeShop.Messaging.Kafka.Retry;

public sealed class KafkaMessagingOptions
{
    public const string SectionName = "Messaging:Kafka";

    public string BootstrapServers { get; set; } = string.Empty;
    public string TopicPrefix { get; set; } = "coffeeshop";
    public string ConsumerGroupPrefix { get; set; } = "coffeeshop";

    public KafkaRetryOptions Retry { get; set; } = new();
}
