using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Messaging.Kafka.Retry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeeShop.Messaging.IntegrationTests.Retry;

[Collection(KafkaCollection.Name)]
public sealed class KafkaRetryAndDeadLetterTests(KafkaFixture fixture)
{
    [Fact]
    public async Task Transient_failure_recovers_from_retry_topic_with_original_identity()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var names = await CreateTopicFamilyAsync();
        var handler = new ScriptedHandler(new IOException("temporary"), null);
        var delay = new RecordingRetryDelay();
        using var host = BuildHost(names, handler, delay);
        await host.StartAsync(timeout.Token);

        var expected = CreateEnvelope();
        await PublishAsync(host, expected, timeout.Token);
        using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var recovered = await handler.ReadSuccessAsync(recoveryTimeout.Token);

        Assert.Equal(expected.MessageId, recovered.Message.MessageId);
        Assert.Equal([1, 2], handler.Attempts);
        Assert.Single(delay.NotBeforeValues);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Exhausted_transient_failure_is_preserved_in_dlt_after_three_stages()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var names = await CreateTopicFamilyAsync();
        var handler = new ScriptedHandler(
            new IOException("first"),
            new IOException("second"),
            new IOException("third"));
        var delay = new RecordingRetryDelay();
        using var host = BuildHost(names, handler, delay);
        await host.StartAsync(timeout.Token);

        var expected = CreateEnvelope();
        await PublishAsync(host, expected, timeout.Token);
        var deadLetter = await ConsumeAsync(
            names.DeadLetter,
            $"dlt-reader-{names.RunId}",
            timeout.Token);

        Assert.Equal(expected.Payload.OrderId.ToString("D"), deadLetter.Message.Key);
        Assert.Equal(expected.MessageId.ToString("D"), ReadHeader(deadLetter, KafkaHeaderNames.MessageId));
        Assert.Equal("3", ReadHeader(deadLetter, KafkaHeaderNames.DeliveryAttempt));
        Assert.Equal("Transient", ReadHeader(deadLetter, KafkaHeaderNames.FailureKind));
        Assert.Equal("processing-transient", ReadHeader(deadLetter, KafkaHeaderNames.FailureCode));
        Assert.Equal([1, 2, 3], handler.Attempts);
        Assert.Equal(2, delay.NotBeforeValues.Count);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Permanent_failure_goes_directly_to_dlt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var names = await CreateTopicFamilyAsync();
        var handler = new ScriptedHandler(new ArgumentException("invalid item"));
        using var host = BuildHost(names, handler, new RecordingRetryDelay());
        await host.StartAsync(timeout.Token);

        await PublishAsync(host, CreateEnvelope(), timeout.Token);
        var deadLetter = await ConsumeAsync(
            names.DeadLetter,
            $"permanent-reader-{names.RunId}",
            timeout.Token);

        Assert.Equal("Permanent", ReadHeader(deadLetter, KafkaHeaderNames.FailureKind));
        Assert.Equal("invalid-message", ReadHeader(deadLetter, KafkaHeaderNames.FailureCode));
        Assert.Equal("1", ReadHeader(deadLetter, KafkaHeaderNames.DeliveryAttempt));
        Assert.Equal([1], handler.Attempts);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Failed_forward_is_redelivered_before_a_retry_can_succeed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var names = await CreateTopicFamilyAsync();
        var handler = new ScriptedHandler(
            new IOException("first forwarding attempt"),
            new IOException("redelivered original"),
            null);
        using var retryPublisher = new FailOnceRetryPublisher(fixture.BootstrapServers);
        using var host = BuildHost(
            names,
            handler,
            new RecordingRetryDelay(),
            services => services.AddSingleton<IKafkaRetryPublisher>(retryPublisher));
        await host.StartAsync(timeout.Token);

        await PublishAsync(host, CreateEnvelope(), timeout.Token);
        await handler.ReadSuccessAsync(timeout.Token);

        Assert.Equal([1, 1, 2], handler.Attempts);
        Assert.Equal(2, retryPublisher.PublishAttempts);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cancellation_is_neither_forwarded_nor_committed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var names = await CreateTopicFamilyAsync();
        var cancelledHandler = new ScriptedHandler(new OperationCanceledException());
        var envelope = CreateEnvelope();

        using (var cancelledHost = BuildHost(
            names,
            cancelledHandler,
            new RecordingRetryDelay(),
            services => services.Configure<HostOptions>(options =>
                options.BackgroundServiceExceptionBehavior =
                    BackgroundServiceExceptionBehavior.Ignore)))
        {
            await cancelledHost.StartAsync(timeout.Token);
            await PublishAsync(cancelledHost, envelope, timeout.Token);
            await cancelledHandler.WaitForInvocationAsync(timeout.Token);
            await cancelledHost.StopAsync(CancellationToken.None);
        }

        var recoveryHandler = new ScriptedHandler((Exception?)null);
        using (var recoveryHost = BuildHost(
            names,
            recoveryHandler,
            new RecordingRetryDelay()))
        {
            await recoveryHost.StartAsync(timeout.Token);
            var recovered = await recoveryHandler.ReadSuccessAsync(timeout.Token);
            Assert.Equal(envelope.MessageId, recovered.Message.MessageId);
            Assert.Equal([1], recoveryHandler.Attempts);
            await recoveryHost.StopAsync(CancellationToken.None);
        }

        Assert.Null(await TryConsumeAsync(
            names.RetryOne,
            $"cancel-retry-reader-{names.RunId}",
            TimeSpan.FromMilliseconds(500)));
        Assert.Null(await TryConsumeAsync(
            names.DeadLetter,
            $"cancel-dlt-reader-{names.RunId}",
            TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Waiting_retry_consumer_does_not_block_original_topic_consumer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var names = await CreateTopicFamilyAsync();
        var handler = new ScriptedHandler();
        var delay = new BlockingRetryDelay();
        await ProduceRetryOneAsync(names, CreateEnvelope(), timeout.Token);
        using var host = BuildHost(names, handler, delay);
        await host.StartAsync(timeout.Token);

        try
        {
            await delay.Started.Task.WaitAsync(timeout.Token);
            var expected = CreateEnvelope();
            await PublishAsync(host, expected, timeout.Token);
            using var originalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var received = await handler.ReadSuccessAsync(originalTimeout.Token);

            Assert.Equal(expected.MessageId, received.Message.MessageId);
            Assert.Contains(names.Original, received.Context.Source, StringComparison.Ordinal);
        }
        finally
        {
            delay.Release.TrySetResult();
            await host.StopAsync(CancellationToken.None);
        }
    }

    private IHost BuildHost(
        TopicFamily names,
        ScriptedHandler handler,
        IRetryDelay retryDelay,
        Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IRetryDelay>(retryDelay);
        configure?.Invoke(builder.Services);
        builder.Services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = fixture.BootstrapServers;
            options.TopicPrefix = names.Prefix;
            options.ConsumerGroupPrefix = names.GroupPrefix;
        });
        builder.Services.AddSingleton(handler);
        builder.Services.AddKafkaConsumer<OrderPlacedV1, ScriptedHandler>("lesson26");
        return builder.Build();
    }

    private static async Task PublishAsync(
        IHost host,
        IntegrationEventEnvelope<OrderPlacedV1> envelope,
        CancellationToken cancellationToken)
    {
        var publisher = host.Services.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(
            envelope.Payload.OrderId.ToString("D"),
            envelope,
            new MessageIdentity(
                envelope.CorrelationId,
                envelope.CausationId,
                null,
                null),
            cancellationToken);
    }

    private async Task ProduceRetryOneAsync(
        TopicFamily names,
        IntegrationEventEnvelope<OrderPlacedV1> envelope,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = fixture.BootstrapServers;
            options.ProducerFormat = KafkaProducerFormat.Json;
        });
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<KafkaIntegrationEventMapper>();
        var message = await mapper.ToMessageAsync(
            names.Original,
            envelope.Payload.OrderId.ToString("D"),
            envelope,
            cancellationToken);
        message.Headers.Add(
            KafkaHeaderNames.DeliveryAttempt,
            Encoding.UTF8.GetBytes("2"));
        message.Headers.Add(
            KafkaHeaderNames.NotBefore,
            Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.AddSeconds(1).ToString("O")));
        message.Headers.Add(
            KafkaHeaderNames.OriginalTopic,
            Encoding.UTF8.GetBytes(names.Original));
        message.Headers.Add(
            KafkaHeaderNames.OriginalPartition,
            Encoding.UTF8.GetBytes("0"));
        message.Headers.Add(
            KafkaHeaderNames.OriginalOffset,
            Encoding.UTF8.GetBytes("0"));
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = fixture.BootstrapServers,
            Acks = Acks.All
        }).Build();
        await producer.ProduceAsync(names.RetryOne, message, cancellationToken);
    }

    private async Task<TopicFamily> CreateTopicFamilyAsync()
    {
        var runId = Guid.NewGuid().ToString("N");
        var prefix = $"lesson26-{runId}";
        var original = $"{prefix}.orders.v1";
        var family = new TopicFamily(
            runId,
            prefix,
            $"lesson26-{runId}",
            original,
            $"{original}.retry.1",
            $"{original}.retry.2",
            $"{original}.dlt");
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = fixture.BootstrapServers
        }).Build();
        await admin.CreateTopicsAsync(
            new[] { family.Original, family.RetryOne, family.RetryTwo, family.DeadLetter }
                .Select(topic => new TopicSpecification
                {
                    Name = topic,
                    NumPartitions = 1,
                    ReplicationFactor = 1
                }));
        return family;
    }

    private async Task<ConsumeResult<string, byte[]>> ConsumeAsync(
        string topic,
        string groupId,
        CancellationToken cancellationToken)
    {
        using var consumer = CreateConsumer(groupId);
        consumer.Subscribe(topic);
        return await Task.Run(() => consumer.Consume(cancellationToken), cancellationToken);
    }

    private async Task<ConsumeResult<string, byte[]>?> TryConsumeAsync(
        string topic,
        string groupId,
        TimeSpan timeout)
    {
        using var consumer = CreateConsumer(groupId);
        consumer.Subscribe(topic);
        return await Task.Run(() => consumer.Consume(timeout));
    }

    private IConsumer<string, byte[]> CreateConsumer(string groupId) =>
        new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = fixture.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();

    private static string ReadHeader(
        ConsumeResult<string, byte[]> record,
        string name) => Encoding.UTF8.GetString(record.Message.Headers.GetLastBytes(name));

    private static IntegrationEventEnvelope<OrderPlacedV1> CreateEnvelope()
    {
        var messageId = Guid.NewGuid();
        return new IntegrationEventEnvelope<OrderPlacedV1>(
            messageId,
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            DateTimeOffset.UtcNow,
            $"workflow-{messageId:N}",
            null,
            new OrderPlacedV1(
                Guid.NewGuid(),
                [new OrderLineItemV1(Guid.NewGuid(), "Latte", "Barista")]));
    }

    private sealed class RecordingRetryDelay : IRetryDelay
    {
        internal ConcurrentQueue<DateTimeOffset> NotBeforeValues { get; } = new();

        public Task DelayUntilAsync(
            DateTimeOffset notBeforeUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NotBeforeValues.Enqueue(notBeforeUtc);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingRetryDelay : IRetryDelay
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DelayUntilAsync(
            DateTimeOffset notBeforeUtc,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ScriptedHandler(params Exception?[] outcomes)
        : IIntegrationEventHandler<OrderPlacedV1>
    {
        private readonly ConcurrentQueue<Exception?> _outcomes = new(outcomes);
        private readonly Channel<ReceivedMessage> _successes =
            Channel.CreateUnbounded<ReceivedMessage>();
        private readonly Channel<bool> _invocations = Channel.CreateUnbounded<bool>();

        internal ConcurrentQueue<int> Attempts { get; } = new();

        public async Task HandleAsync(
            IntegrationEventEnvelope<OrderPlacedV1> message,
            IntegrationMessageContext context,
            CancellationToken cancellationToken)
        {
            Attempts.Enqueue(context.DeliveryAttempt);
            await _invocations.Writer.WriteAsync(true, cancellationToken);
            if (_outcomes.TryDequeue(out var exception) && exception is not null)
            {
                throw exception;
            }

            await _successes.Writer.WriteAsync(
                new ReceivedMessage(message, context),
                cancellationToken);
        }

        internal async Task<ReceivedMessage> ReadSuccessAsync(
            CancellationToken cancellationToken) =>
            await _successes.Reader.ReadAsync(cancellationToken);

        internal async Task WaitForInvocationAsync(CancellationToken cancellationToken) =>
            _ = await _invocations.Reader.ReadAsync(cancellationToken);
    }

    private sealed class FailOnceRetryPublisher : IKafkaRetryPublisher, IDisposable
    {
        private readonly IProducer<string, byte[]> _producer;
        private int _publishAttempts;

        internal FailOnceRetryPublisher(string bootstrapServers)
        {
            _producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true
            }).Build();
        }

        internal int PublishAttempts => Volatile.Read(ref _publishAttempts);

        public async Task PublishAsync(
            string topic,
            Message<string, byte[]> message,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _publishAttempts) == 1)
            {
                throw new KafkaException(new Error(ErrorCode.Local_Transport));
            }

            await _producer.ProduceAsync(topic, message, cancellationToken);
        }

        public void Dispose() => _producer.Dispose();
    }

    private sealed record TopicFamily(
        string RunId,
        string Prefix,
        string GroupPrefix,
        string Original,
        string RetryOne,
        string RetryTwo,
        string DeadLetter);

    private sealed record ReceivedMessage(
        IntegrationEventEnvelope<OrderPlacedV1> Message,
        IntegrationMessageContext Context);
}
