using System.Globalization;
using System.Text;
using Confluent.Kafka;
using CoffeeShop.Messaging.Abstractions;

namespace CoffeeShop.Messaging.Kafka.Retry;

internal sealed record DeadLetterMetadata(
    string OriginalTopic,
    int OriginalPartition,
    long OriginalOffset,
    int DeliveryAttempt,
    IntegrationFailureKind FailureKind,
    string SafeErrorCode,
    DateTimeOffset FailedAtUtc)
{
    internal static DeadLetterMetadata FromHeaders(Headers headers) => new(
        Read(headers, KafkaHeaderNames.OriginalTopic),
        int.Parse(
            Read(headers, KafkaHeaderNames.OriginalPartition),
            CultureInfo.InvariantCulture),
        long.Parse(
            Read(headers, KafkaHeaderNames.OriginalOffset),
            CultureInfo.InvariantCulture),
        int.Parse(
            Read(headers, KafkaHeaderNames.DeliveryAttempt),
            CultureInfo.InvariantCulture),
        Enum.Parse<IntegrationFailureKind>(
            Read(headers, KafkaHeaderNames.FailureKind),
            ignoreCase: true),
        Read(headers, KafkaHeaderNames.FailureCode),
        DateTimeOffset.Parse(
            Read(headers, KafkaHeaderNames.FailureAt),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind));

    private static string Read(Headers headers, string name) =>
        Encoding.UTF8.GetString(headers.GetLastBytes(name));
}
