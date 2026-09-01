using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Messaging.Kafka.Avro;
using System.Text.Json;

namespace CoffeeShop.MessagingTests.Avro;

public sealed class DualFormatIntegrationEventCodecTests
{
    [Fact]
    public async Task Json_v1_fixture_remains_readable_after_dual_reader_rollout()
    {
        var codec = new DualFormatIntegrationEventCodec(
            new JsonIntegrationEventCodec(),
            new RejectAvroCodec());
        var json = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "order-placed-v1.json"));

        var envelope = await codec.DeserializeAsync<OrderPlacedV1>(
            "coffeeshop.orders.v1",
            json,
            "application/json",
            CancellationToken.None);

        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            envelope.MessageId);
        Assert.Equal(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            envelope.Payload.OrderId);
        Assert.Equal(2, envelope.Payload.Items.Count);
    }

    [Fact]
    public async Task Unknown_content_type_is_rejected_as_a_contract_failure()
    {
        var codec = new DualFormatIntegrationEventCodec(
            new JsonIntegrationEventCodec(),
            new RejectAvroCodec());

        var exception = await Assert.ThrowsAsync<JsonException>(async () =>
            await codec.DeserializeAsync<OrderPlacedV1>(
                "coffeeshop.orders.v1",
                ReadOnlyMemory<byte>.Empty,
                "application/x-unknown",
                CancellationToken.None));

        Assert.Equal(
            "Kafka content type 'application/x-unknown' is not supported.",
            exception.Message);
    }

    [Fact]
    public async Task Avro_producer_format_selects_Avro_bytes_and_content_type()
    {
        var avroCodec = new RecordingAvroCodec([0, 0, 0, 1, 42]);
        var codec = new DualFormatIntegrationEventCodec(
            new JsonIntegrationEventCodec(),
            avroCodec);
        var envelope = CreateEnvelope();

        var encoded = await codec.SerializeAsync(
            "coffeeshop.orders.v1",
            envelope,
            KafkaProducerFormat.Avro,
            CancellationToken.None);

        Assert.Equal("application/avro", encoded.ContentType);
        Assert.Equal([0, 0, 0, 1, 42], encoded.Value);
        Assert.Same(envelope, avroCodec.Envelope);
        Assert.Equal("coffeeshop.orders.v1", avroCodec.Topic);
    }

    private static IntegrationEventEnvelope<OrderPlacedV1> CreateEnvelope() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            DateTimeOffset.Parse("2026-08-26T01:02:03+00:00"),
            "order-workflow-11111111",
            null,
            new OrderPlacedV1(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                []));

    private sealed class RejectAvroCodec : IAvroIntegrationEventCodec
    {
        public ValueTask<byte[]> SerializeAsync<TPayload>(
            string topic,
            IntegrationEventEnvelope<TPayload> envelope,
            CancellationToken cancellationToken)
            where TPayload : IIntegrationEvent =>
            throw new Xunit.Sdk.XunitException("JSON decoding must not call Avro.");

        public ValueTask<IntegrationEventEnvelope<TPayload>> DeserializeAsync<TPayload>(
            string topic,
            ReadOnlyMemory<byte> value,
            CancellationToken cancellationToken)
            where TPayload : IIntegrationEvent =>
            throw new Xunit.Sdk.XunitException("JSON decoding must not call Avro.");
    }

    private sealed class RecordingAvroCodec(byte[] value) : IAvroIntegrationEventCodec
    {
        public string? Topic { get; private set; }
        public object? Envelope { get; private set; }

        public ValueTask<byte[]> SerializeAsync<TPayload>(
            string topic,
            IntegrationEventEnvelope<TPayload> envelope,
            CancellationToken cancellationToken)
            where TPayload : IIntegrationEvent
        {
            Topic = topic;
            Envelope = envelope;
            return ValueTask.FromResult(value);
        }

        public ValueTask<IntegrationEventEnvelope<TPayload>> DeserializeAsync<TPayload>(
            string topic,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
            where TPayload : IIntegrationEvent =>
            throw new Xunit.Sdk.XunitException("Serialization must not decode Avro.");
    }
}
