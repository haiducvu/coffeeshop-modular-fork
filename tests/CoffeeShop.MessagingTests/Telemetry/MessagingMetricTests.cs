using System.Diagnostics.Metrics;
using CoffeeShop.Messaging.Abstractions;

namespace CoffeeShop.MessagingTests.Telemetry;

public sealed class MessagingMetricTests
{
    private static readonly HashSet<string> AllowedTagNames =
    [
        "event.type",
        "module",
        "messaging.destination.name",
        "operation",
        "result",
        "retry.level"
    ];

    [Fact]
    public void Messaging_metrics_cover_reliability_without_high_cardinality_dimensions()
    {
        var measurements = new List<MeasurementRecord>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == MessagingTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new MeasurementRecord(
                instrument.Name,
                value,
                tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new MeasurementRecord(
                instrument.Name,
                value,
                tags.ToArray())));
        listener.Start();

        MessagingTelemetry.RecordPublish(
            "coffeeshop.order-placed",
            "coffeeshop.orders.v1",
            "success",
            TimeSpan.FromMilliseconds(12));
        MessagingTelemetry.RecordConsume(
            "coffeeshop.order-placed",
            "coffeeshop.orders.v1",
            "barista",
            "success",
            TimeSpan.FromMilliseconds(18));
        MessagingTelemetry.RecordOutboxBatch("counter", pendingCount: 3);
        MessagingTelemetry.RecordOutboxPublish("counter", "coffeeshop.order-placed", "failure");
        MessagingTelemetry.RecordInboxDuplicate("barista", "coffeeshop.order-placed");
        MessagingTelemetry.RecordForwarded(
            "coffeeshop.order-placed",
            "coffeeshop.orders.v1.retry.1",
            retryLevel: 1,
            deadLetter: false);
        MessagingTelemetry.RecordForwarded(
            "coffeeshop.order-placed",
            "coffeeshop.orders.v1.dlt",
            retryLevel: 2,
            deadLetter: true);

        Assert.Equal(
            [
                "coffeeshop.messaging.consume.count",
                "coffeeshop.messaging.deadletter.forwarded",
                "coffeeshop.messaging.inbox.duplicates",
                "coffeeshop.messaging.outbox.pending",
                "coffeeshop.messaging.outbox.publish.attempts",
                "coffeeshop.messaging.outbox.publish.failures",
                "coffeeshop.messaging.processing.duration",
                "coffeeshop.messaging.publish.count",
                "coffeeshop.messaging.retry.forwarded"
            ],
            measurements.Select(measurement => measurement.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        Assert.All(measurements, measurement =>
        {
            Assert.All(measurement.Tags, tag => Assert.Contains(tag.Key, AllowedTagNames));
            var rendered = string.Join(
                '|',
                measurement.Tags.Select(tag => $"{tag.Key}={tag.Value}"));
            Assert.DoesNotContain("order.id", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("message.id", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("correlation", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("loyalty", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payload", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", rendered, StringComparison.OrdinalIgnoreCase);
        });
    }

    private sealed record MeasurementRecord(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);
}
