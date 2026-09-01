using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Confluent.SchemaRegistry;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Kafka.Avro;

namespace CoffeeShop.Messaging.IntegrationTests.Avro;

public sealed class AvroCancellationTests
{
    [Fact]
    public async Task Pending_schema_registry_call_releases_the_publisher_on_cancellation()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var schemaRegistry = new CachedSchemaRegistryClient(new SchemaRegistryConfig
        {
            Url = $"http://127.0.0.1:{port}",
            MaxRetries = 1,
            RequestTimeoutMs = 30_000
        });
        var codec = new AvroIntegrationEventCodec(schemaRegistry);
        using var cancellation = new CancellationTokenSource();

        var pendingSerialization = codec.SerializeAsync(
                "coffeeshop.orders.v1",
                CreateEnvelope(),
                cancellation.Token)
            .AsTask();
        var accepting = listener.AcceptTcpClientAsync();
        var first = await Task.WhenAny(
            accepting,
            pendingSerialization,
            Task.Delay(TimeSpan.FromSeconds(5)));
        if (first == pendingSerialization)
        {
            await pendingSerialization;
        }

        Assert.Same(accepting, first);
        using var connection = await accepting;
        var stopwatch = Stopwatch.StartNew();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pendingSerialization);

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Cancellation took {stopwatch.Elapsed} while Schema Registry was still pending.");
        listener.Stop();
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
}
