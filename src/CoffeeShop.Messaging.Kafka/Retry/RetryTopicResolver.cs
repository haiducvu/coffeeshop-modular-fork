namespace CoffeeShop.Messaging.Kafka.Retry;

internal enum KafkaConsumerStage
{
    Original,
    RetryOne,
    RetryTwo
}

internal static class RetryTopicResolver
{
    private const string RetryOneSuffix = ".retry.1";
    private const string RetryTwoSuffix = ".retry.2";
    private const string DeadLetterSuffix = ".dlt";

    internal static string ResolveRetryOne(string originalTopic) =>
        $"{originalTopic}{RetryOneSuffix}";

    internal static string ResolveRetryTwo(string originalTopic) =>
        $"{originalTopic}{RetryTwoSuffix}";

    internal static string ResolveDeadLetter(string originalTopic) =>
        $"{originalTopic}{DeadLetterSuffix}";

    internal static string ResolveConsumerTopic(
        string originalTopic,
        KafkaConsumerStage stage) => stage switch
        {
            KafkaConsumerStage.Original => originalTopic,
            KafkaConsumerStage.RetryOne => ResolveRetryOne(originalTopic),
            KafkaConsumerStage.RetryTwo => ResolveRetryTwo(originalTopic),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };

    internal static string ResolveConsumerGroupRole(
        string consumerRole,
        KafkaConsumerStage stage) => stage switch
        {
            KafkaConsumerStage.Original => consumerRole,
            KafkaConsumerStage.RetryOne => $"{consumerRole}.retry.1",
            KafkaConsumerStage.RetryTwo => $"{consumerRole}.retry.2",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };

    internal static KafkaConsumerStage ResolveStage(
        string originalTopic,
        string topic)
    {
        if (string.Equals(topic, originalTopic, StringComparison.Ordinal))
        {
            return KafkaConsumerStage.Original;
        }

        if (string.Equals(topic, ResolveRetryOne(originalTopic), StringComparison.Ordinal))
        {
            return KafkaConsumerStage.RetryOne;
        }

        if (string.Equals(topic, ResolveRetryTwo(originalTopic), StringComparison.Ordinal))
        {
            return KafkaConsumerStage.RetryTwo;
        }

        throw new ArgumentException(
            "Kafka record did not come from the configured topic family.",
            nameof(topic));
    }

    internal static int ResolveDeliveryAttempt(
        string originalTopic,
        string topic) => ResolveStage(originalTopic, topic) switch
        {
            KafkaConsumerStage.Original => 1,
            KafkaConsumerStage.RetryOne => 2,
            KafkaConsumerStage.RetryTwo => 3,
            _ => throw new InvalidOperationException("Unknown Kafka consumer stage.")
        };
}
