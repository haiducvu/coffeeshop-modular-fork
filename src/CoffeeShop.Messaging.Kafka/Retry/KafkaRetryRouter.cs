using System.Globalization;
using System.Text;
using Confluent.Kafka;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Kafka.Retry;

internal sealed class KafkaRetryRouter(
    IOptions<KafkaMessagingOptions> options,
    IIntegrationFailureClassifier failureClassifier,
    IKafkaRetryPublisher publisher,
    IRetryDelay retryDelay,
    TimeProvider timeProvider)
{
    private static readonly string[] PreservedHeaderNames =
    [
        KafkaHeaderNames.MessageId,
        KafkaHeaderNames.EventType,
        KafkaHeaderNames.EventVersion,
        KafkaHeaderNames.OccurredAt,
        KafkaHeaderNames.CorrelationId,
        KafkaHeaderNames.CausationId,
        KafkaHeaderNames.ContentType,
        KafkaHeaderNames.TraceParent,
        KafkaHeaderNames.TraceState
    ];

    private readonly KafkaRetryOptions _retryOptions = options.Value.Retry;

    internal async Task DelayIfNeededAsync(
        string originalTopic,
        string sourceTopic,
        Message<string, byte[]> message,
        CancellationToken cancellationToken)
    {
        var stage = RetryTopicResolver.ResolveStage(originalTopic, sourceTopic);
        if (stage == KafkaConsumerStage.Original)
        {
            return;
        }

        var value = FindHeader(message.Headers, KafkaHeaderNames.NotBefore);
        if (value is null)
        {
            throw new FormatException("Kafka retry record has no not-before header.");
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var notBeforeUtc))
        {
            throw new FormatException("Kafka retry record has an invalid not-before header.");
        }

        var maximumDelay = stage == KafkaConsumerStage.RetryOne
            ? _retryOptions.FirstDelay
            : _retryOptions.SecondDelay;
        if (notBeforeUtc > timeProvider.GetUtcNow().Add(maximumDelay))
        {
            throw new FormatException("Kafka retry record exceeds its configured delay.");
        }

        await retryDelay.DelayUntilAsync(notBeforeUtc, cancellationToken);
    }

    internal int ResolveDeliveryAttempt(string originalTopic, string topic) =>
        RetryTopicResolver.ResolveDeliveryAttempt(originalTopic, topic);

    internal async Task RouteAsync(
        string originalTopic,
        ConsumeResult<string, byte[]> source,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is OperationCanceledException cancellation)
        {
            throw cancellation;
        }

        var failure = failureClassifier.Classify(exception);
        var safeErrorCode = IsSafeErrorCode(failure.SafeErrorCode)
            ? failure.SafeErrorCode
            : failure.Kind == IntegrationFailureKind.Permanent
                ? "processing-permanent"
                : "processing-transient";
        var stage = RetryTopicResolver.ResolveStage(originalTopic, source.Topic);
        var currentAttempt = RetryTopicResolver.ResolveDeliveryAttempt(
            originalTopic,
            source.Topic);
        var target = ResolveTarget(originalTopic, currentAttempt, failure.Kind);
        var nextAttempt = ResolveTargetAttempt(currentAttempt, target);
        var failedAtUtc = timeProvider.GetUtcNow();
        var forwarded = CloneMessageWithAllowedHeaders(source.Message);
        var originalPartition = stage == KafkaConsumerStage.Original
            ? source.Partition.Value
            : ReadNonNegativeInt(
                source.Message.Headers,
                KafkaHeaderNames.OriginalPartition,
                source.Partition.Value);
        var originalOffset = stage == KafkaConsumerStage.Original
            ? source.Offset.Value
            : ReadNonNegativeLong(
                source.Message.Headers,
                KafkaHeaderNames.OriginalOffset,
                source.Offset.Value);

        Set(forwarded.Headers, KafkaHeaderNames.DeliveryAttempt, nextAttempt);
        Set(forwarded.Headers, KafkaHeaderNames.OriginalTopic, originalTopic);
        Set(
            forwarded.Headers,
            KafkaHeaderNames.OriginalPartition,
            originalPartition.ToString(CultureInfo.InvariantCulture));
        Set(
            forwarded.Headers,
            KafkaHeaderNames.OriginalOffset,
            originalOffset.ToString(CultureInfo.InvariantCulture));
        Set(forwarded.Headers, KafkaHeaderNames.FailureKind, failure.Kind.ToString());
        Set(forwarded.Headers, KafkaHeaderNames.FailureCode, safeErrorCode);
        Set(
            forwarded.Headers,
            KafkaHeaderNames.FailureAt,
            failedAtUtc.ToString("O", CultureInfo.InvariantCulture));

        if (target.EndsWith(".dlt", StringComparison.Ordinal))
        {
            forwarded.Headers.Remove(KafkaHeaderNames.NotBefore);
        }
        else
        {
            var delay = nextAttempt == 2
                ? _retryOptions.FirstDelay
                : _retryOptions.SecondDelay;
            Set(
                forwarded.Headers,
                KafkaHeaderNames.NotBefore,
                failedAtUtc.Add(delay).ToString("O", CultureInfo.InvariantCulture));
        }

        await publisher.PublishAsync(target, forwarded, cancellationToken);
    }

    private static string ResolveTarget(
        string originalTopic,
        int currentAttempt,
        IntegrationFailureKind failureKind)
    {
        if (failureKind == IntegrationFailureKind.Permanent || currentAttempt >= 3)
        {
            return RetryTopicResolver.ResolveDeadLetter(originalTopic);
        }

        return currentAttempt == 1
            ? RetryTopicResolver.ResolveRetryOne(originalTopic)
            : RetryTopicResolver.ResolveRetryTwo(originalTopic);
    }

    private static int ResolveTargetAttempt(int currentAttempt, string target) =>
        target.EndsWith(".dlt", StringComparison.Ordinal)
            ? currentAttempt
            : currentAttempt + 1;

    private static Message<string, byte[]> CloneMessageWithAllowedHeaders(
        Message<string, byte[]> source) => new()
    {
        Key = source.Key,
        Value = source.Value,
        Headers = CopyAllowedHeaders(source.Headers),
        Timestamp = source.Timestamp
    };

    private static Headers CopyAllowedHeaders(Headers source)
    {
        var headers = new Headers();
        foreach (var name in PreservedHeaderNames)
        {
            var header = source.LastOrDefault(candidate =>
                string.Equals(candidate.Key, name, StringComparison.Ordinal));
            if (header is not null)
            {
                headers.Add(header.Key, header.GetValueBytes());
            }
        }

        return headers;
    }

    private static void Set(Headers headers, string name, int value) =>
        Set(headers, name, value.ToString(CultureInfo.InvariantCulture));

    private static void Set(Headers headers, string name, string value)
    {
        headers.Remove(name);
        headers.Add(name, Encoding.UTF8.GetBytes(value));
    }

    private static int ReadNonNegativeInt(
        Headers headers,
        string name,
        int fallback)
    {
        var value = FindHeader(headers, name);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0
                ? parsed
                : fallback;
    }

    private static long ReadNonNegativeLong(
        Headers headers,
        string name,
        long fallback)
    {
        var value = FindHeader(headers, name);
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0
                ? parsed
                : fallback;
    }

    private static string? FindHeader(Headers headers, string name)
    {
        var header = headers.LastOrDefault(candidate =>
            string.Equals(candidate.Key, name, StringComparison.Ordinal));
        return header is null
            ? null
            : Encoding.UTF8.GetString(header.GetValueBytes());
    }

    private static bool IsSafeErrorCode(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 64
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '-');
}
