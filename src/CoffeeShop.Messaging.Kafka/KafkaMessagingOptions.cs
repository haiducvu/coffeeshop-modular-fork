namespace CoffeeShop.Messaging.Kafka;

using CoffeeShop.Messaging.Kafka.Retry;

public sealed class KafkaMessagingOptions
{
    public const string SectionName = "Messaging:Kafka";

    public string BootstrapServers { get; set; } = string.Empty;
    public string SchemaRegistryUrl { get; set; } = "http://localhost:8081";
    public KafkaProducerFormat ProducerFormat { get; set; } = KafkaProducerFormat.Json;
    public string TopicPrefix { get; set; } = "coffeeshop";
    public string ConsumerGroupPrefix { get; set; } = "coffeeshop";

    public KafkaRetryOptions Retry { get; set; } = new();
}
