using System.Text;
using Confluent.Kafka;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Messaging.Kafka.Retry;
using Microsoft.Extensions.Options;

namespace CoffeeShop.MessagingTests.Retry;

public sealed class KafkaRetryRouterTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-08-28T02:00:00+00:00");

    [Fact]
    public async Task Transient_failure_moves_through_two_delayed_topics_then_dlt()
    {
        var time = new ManualTimeProvider(Start);
        var delay = new AdvancingRetryDelay(time);
        var publisher = new RecordingRetryPublisher();
        var router = CreateRouter(publisher, delay, time);
        var original = CreateResult("lesson26.orders.v1", CreateMessage(), 4, 12);

        await router.RouteAsync(
            "lesson26.orders.v1",
            original,
            new IOException("database unavailable"),
            default);
        var retryOne = Assert.Single(publisher.Published);
        Assert.Equal("lesson26.orders.v1.retry.1", retryOne.Topic);
        Assert.Equal("2", ReadHeader(retryOne.Message, KafkaHeaderNames.DeliveryAttempt));
        Assert.Equal(Start.AddSeconds(1), ReadInstant(retryOne.Message, KafkaHeaderNames.NotBefore));
        AssertOriginalRecordWasPreserved(original, retryOne.Message);

        await router.DelayIfNeededAsync(
            "lesson26.orders.v1",
            retryOne.Topic,
            retryOne.Message,
            default);
        Assert.Equal([Start.AddSeconds(1)], delay.NotBeforeValues);
        var retryOneResult = CreateResult(retryOne.Topic, retryOne.Message, 0, 20);
        await router.RouteAsync(
            "lesson26.orders.v1",
            retryOneResult,
            new IOException("database unavailable again"),
            default);

        var retryTwo = publisher.Published[1];
        Assert.Equal("lesson26.orders.v1.retry.2", retryTwo.Topic);
        Assert.Equal("3", ReadHeader(retryTwo.Message, KafkaHeaderNames.DeliveryAttempt));
        Assert.Equal(
            Start.AddSeconds(6),
            ReadInstant(retryTwo.Message, KafkaHeaderNames.NotBefore));

        await router.DelayIfNeededAsync(
            "lesson26.orders.v1",
            retryTwo.Topic,
            retryTwo.Message,
            default);
        Assert.Equal(
            [Start.AddSeconds(1), Start.AddSeconds(6)],
            delay.NotBeforeValues);
        var retryTwoResult = CreateResult(retryTwo.Topic, retryTwo.Message, 0, 21);
        await router.RouteAsync(
            "lesson26.orders.v1",
            retryTwoResult,
            new IOException("retry budget exhausted"),
            default);

        var deadLetter = publisher.Published[2];
        Assert.Equal("lesson26.orders.v1.dlt", deadLetter.Topic);
        Assert.Equal("3", ReadHeader(deadLetter.Message, KafkaHeaderNames.DeliveryAttempt));
        Assert.Null(FindHeader(deadLetter.Message, KafkaHeaderNames.NotBefore));
        var metadata = DeadLetterMetadata.FromHeaders(deadLetter.Message.Headers);
        Assert.Equal("lesson26.orders.v1", metadata.OriginalTopic);
        Assert.Equal(4, metadata.OriginalPartition);
        Assert.Equal(12, metadata.OriginalOffset);
        Assert.Equal(3, metadata.DeliveryAttempt);
        Assert.Equal(IntegrationFailureKind.Transient, metadata.FailureKind);
        Assert.Equal("processing-transient", metadata.SafeErrorCode);
    }

    [Fact]
    public async Task Permanent_failure_goes_directly_to_dlt_without_sensitive_exception_data()
    {
        var time = new ManualTimeProvider(Start);
        var publisher = new RecordingRetryPublisher();
        var router = CreateRouter(
            publisher,
            new AdvancingRetryDelay(time),
            time);
        var source = CreateResult("lesson26.orders.v1", CreateMessage(), 1, 7);
        const string sensitive =
            "Host=secret-db;Password=coffee;Bearer credential-token;stack trace";

        await router.RouteAsync(
            "lesson26.orders.v1",
            source,
            new ArgumentException(sensitive),
            default);

        var deadLetter = Assert.Single(publisher.Published);
        Assert.Equal("lesson26.orders.v1.dlt", deadLetter.Topic);
        var metadata = DeadLetterMetadata.FromHeaders(deadLetter.Message.Headers);
        Assert.Equal(IntegrationFailureKind.Permanent, metadata.FailureKind);
        Assert.Equal("invalid-message", metadata.SafeErrorCode);
        var headerText = string.Join(
            "|",
            deadLetter.Message.Headers.Select(header =>
                $"{header.Key}={Encoding.UTF8.GetString(header.GetValueBytes())}"));
        Assert.DoesNotContain(sensitive, headerText, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", headerText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential-token", headerText, StringComparison.Ordinal);
        Assert.DoesNotContain("stack trace", headerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_is_not_forwarded()
    {
        var time = new ManualTimeProvider(Start);
        var publisher = new RecordingRetryPublisher();
        var router = CreateRouter(
            publisher,
            new AdvancingRetryDelay(time),
            time);
        var source = CreateResult("lesson26.orders.v1", CreateMessage(), 0, 0);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            router.RouteAsync(
                "lesson26.orders.v1",
                source,
                new OperationCanceledException(),
                default));

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Route_completes_only_after_forwarding_is_acknowledged()
    {
        var time = new ManualTimeProvider(Start);
        var publisher = new GatedRetryPublisher();
        var router = CreateRouter(
            publisher,
            new AdvancingRetryDelay(time),
            time);
        var events = publisher.Events;

        var routing = router.RouteAsync(
            "lesson26.orders.v1",
            CreateResult("lesson26.orders.v1", CreateMessage(), 0, 0),
            new IOException("retry me"),
            default);
        await publisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(routing.IsCompleted);
        publisher.Acknowledge.SetResult();
        await routing;
        events.Add("source-offset-commit");

        Assert.Equal(
            ["forward-start", "forward-acknowledged", "source-offset-commit"],
            events);
    }

    [Fact]
    public async Task Forwarding_failure_is_observable_so_the_source_is_not_committed()
    {
        var time = new ManualTimeProvider(Start);
        var router = CreateRouter(
            new FailingRetryPublisher(),
            new AdvancingRetryDelay(time),
            time);

        await Assert.ThrowsAsync<KafkaException>(() =>
            router.RouteAsync(
                "lesson26.orders.v1",
                CreateResult("lesson26.orders.v1", CreateMessage(), 0, 0),
                new IOException("retry me"),
                default));
    }

    [Fact]
    public async Task Original_record_cannot_spoof_retry_routing_delay_or_dlt_headers()
    {
        var time = new ManualTimeProvider(Start);
        var delay = new AdvancingRetryDelay(time);
        var publisher = new RecordingRetryPublisher();
        var router = CreateRouter(publisher, delay, time);
        var message = CreateMessage();
        message.Headers.Add(
            KafkaHeaderNames.OriginalTopic,
            Encoding.UTF8.GetBytes("attacker.redirect"));
        message.Headers.Add(
            KafkaHeaderNames.OriginalPartition,
            Encoding.UTF8.GetBytes("99"));
        message.Headers.Add(
            KafkaHeaderNames.OriginalOffset,
            Encoding.UTF8.GetBytes("999"));
        message.Headers.Add(
            KafkaHeaderNames.NotBefore,
            Encoding.UTF8.GetBytes(Start.AddDays(30).ToString("O")));
        message.Headers.Add(
            "authorization",
            Encoding.UTF8.GetBytes("Bearer secret-token"));
        var source = CreateResult("lesson26.orders.v1", message, 2, 14);

        await router.DelayIfNeededAsync(
            "lesson26.orders.v1",
            source.Topic,
            source.Message,
            default);
        await router.RouteAsync(
            "lesson26.orders.v1",
            source,
            new IOException("temporary"),
            default);

        Assert.Empty(delay.NotBeforeValues);
        var forwarded = Assert.Single(publisher.Published);
        Assert.Equal("lesson26.orders.v1.retry.1", forwarded.Topic);
        Assert.Null(FindHeader(forwarded.Message, "authorization"));
        Assert.Equal(
            "lesson26.orders.v1",
            ReadHeader(forwarded.Message, KafkaHeaderNames.OriginalTopic));
        Assert.Equal("2", ReadHeader(forwarded.Message, KafkaHeaderNames.OriginalPartition));
        Assert.Equal("14", ReadHeader(forwarded.Message, KafkaHeaderNames.OriginalOffset));
    }

    [Fact]
    public async Task Retry_record_cannot_request_a_delay_beyond_its_configured_stage()
    {
        var time = new ManualTimeProvider(Start);
        var router = CreateRouter(
            new RecordingRetryPublisher(),
            new AdvancingRetryDelay(time),
            time);
        var message = CreateMessage();
        message.Headers.Add(
            KafkaHeaderNames.NotBefore,
            Encoding.UTF8.GetBytes(Start.AddMinutes(1).ToString("O")));

        await Assert.ThrowsAsync<FormatException>(() =>
            router.DelayIfNeededAsync(
                "lesson26.orders.v1",
                "lesson26.orders.v1.retry.1",
                message,
                default));
    }

    private static KafkaRetryRouter CreateRouter(
        IKafkaRetryPublisher publisher,
        IRetryDelay delay,
        TimeProvider timeProvider) =>
        new(
            Options.Create(new KafkaMessagingOptions
            {
                BootstrapServers = "unused:9092",
                TopicPrefix = "lesson26",
                ConsumerGroupPrefix = "lesson26",
                Retry = new KafkaRetryOptions
                {
                    FirstDelay = TimeSpan.FromSeconds(1),
                    SecondDelay = TimeSpan.FromSeconds(5),
                    MaxPollInterval = TimeSpan.FromMinutes(5)
                }
            }),
            new DefaultIntegrationFailureClassifier(),
            publisher,
            delay,
            timeProvider);

    private static ConsumeResult<string, byte[]> CreateResult(
        string topic,
        Message<string, byte[]> message,
        int partition,
        long offset) => new()
        {
            Topic = topic,
            Partition = new Partition(partition),
            Offset = new Offset(offset),
            Message = message
        };

    private static Message<string, byte[]> CreateMessage() => new()
    {
        Key = "order-42",
        Value = [1, 2, 3, 4],
        Headers = new Headers
        {
            { KafkaHeaderNames.MessageId, Encoding.UTF8.GetBytes("11111111-1111-1111-1111-111111111111") },
            { KafkaHeaderNames.CorrelationId, Encoding.UTF8.GetBytes("workflow-42") },
            { KafkaHeaderNames.ContentType, Encoding.UTF8.GetBytes("application/json") }
        }
    };

    private static void AssertOriginalRecordWasPreserved(
        ConsumeResult<string, byte[]> original,
        Message<string, byte[]> forwarded)
    {
        Assert.Equal(original.Message.Key, forwarded.Key);
        Assert.Equal(original.Message.Value, forwarded.Value);
        Assert.Equal(
            ReadHeader(original.Message, KafkaHeaderNames.MessageId),
            ReadHeader(forwarded, KafkaHeaderNames.MessageId));
        Assert.Equal(
            ReadHeader(original.Message, KafkaHeaderNames.CorrelationId),
            ReadHeader(forwarded, KafkaHeaderNames.CorrelationId));
    }

    private static DateTimeOffset ReadInstant(
        Message<string, byte[]> message,
        string name) => DateTimeOffset.Parse(
            ReadHeader(message, name),
            System.Globalization.CultureInfo.InvariantCulture);

    private static string ReadHeader(Message<string, byte[]> message, string name) =>
        Encoding.UTF8.GetString(message.Headers.GetLastBytes(name));

    private static IHeader? FindHeader(Message<string, byte[]> message, string name) =>
        message.Headers.LastOrDefault(header => header.Key == name);

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _utcNow = initial;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void AdvanceTo(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class AdvancingRetryDelay(ManualTimeProvider timeProvider) : IRetryDelay
    {
        internal List<DateTimeOffset> NotBeforeValues { get; } = [];

        public Task DelayUntilAsync(
            DateTimeOffset notBeforeUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NotBeforeValues.Add(notBeforeUtc);
            timeProvider.AdvanceTo(notBeforeUtc);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRetryPublisher : IKafkaRetryPublisher
    {
        internal List<PublishedRecord> Published { get; } = [];

        public Task PublishAsync(
            string topic,
            Message<string, byte[]> message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(new PublishedRecord(topic, message));
            return Task.CompletedTask;
        }
    }

    private sealed class GatedRetryPublisher : IKafkaRetryPublisher
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Acknowledge { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<string> Events { get; } = [];

        public async Task PublishAsync(
            string topic,
            Message<string, byte[]> message,
            CancellationToken cancellationToken)
        {
            Events.Add("forward-start");
            Started.SetResult();
            await Acknowledge.Task.WaitAsync(cancellationToken);
            Events.Add("forward-acknowledged");
        }
    }

    private sealed class FailingRetryPublisher : IKafkaRetryPublisher
    {
        public Task PublishAsync(
            string topic,
            Message<string, byte[]> message,
            CancellationToken cancellationToken) =>
            throw new KafkaException(new Error(ErrorCode.Local_Transport));
    }

    private sealed record PublishedRecord(
        string Topic,
        Message<string, byte[]> Message);
}
