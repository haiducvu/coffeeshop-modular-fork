using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CoffeeShop.Messaging.Abstractions;

public static class MessagingTelemetry
{
    public const string ActivitySourceName = "CoffeeShop.Messaging";
    public const string MeterName = "CoffeeShop.Messaging";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Published = Meter.CreateCounter<long>(
        "coffeeshop.messaging.publish.count",
        description: "Number of integration-event publication results.");
    private static readonly Counter<long> Consumed = Meter.CreateCounter<long>(
        "coffeeshop.messaging.consume.count",
        description: "Number of integration-event consumption results.");
    private static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>(
        "coffeeshop.messaging.processing.duration",
        unit: "ms",
        description: "Integration-event publication or consumption duration.");
    private static readonly Histogram<long> OutboxPending = Meter.CreateHistogram<long>(
        "coffeeshop.messaging.outbox.pending",
        unit: "{message}",
        description: "Outbox records claimed in one polling cycle.");
    private static readonly Counter<long> OutboxPublishAttempts = Meter.CreateCounter<long>(
        "coffeeshop.messaging.outbox.publish.attempts");
    private static readonly Counter<long> OutboxPublishFailures = Meter.CreateCounter<long>(
        "coffeeshop.messaging.outbox.publish.failures");
    private static readonly Counter<long> InboxDuplicates = Meter.CreateCounter<long>(
        "coffeeshop.messaging.inbox.duplicates");
    private static readonly Counter<long> RetryForwarded = Meter.CreateCounter<long>(
        "coffeeshop.messaging.retry.forwarded");
    private static readonly Counter<long> DeadLetterForwarded = Meter.CreateCounter<long>(
        "coffeeshop.messaging.deadletter.forwarded");

    public static Activity? StartProducerActivity(
        string messagingSystem,
        string destination,
        string eventType,
        Guid messageId,
        MessageIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messagingSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(identity);
        var parent = ParseParent(identity.TraceParent, identity.TraceState);
        var activity = ActivitySource.StartActivity(
            $"{eventType} publish",
            ActivityKind.Producer,
            parent);
        SetMessagingTags(
            activity,
            messagingSystem,
            destination,
            eventType,
            messageId,
            identity.CorrelationId);
        return activity;
    }

    public static Activity? StartConsumerActivity(
        string messagingSystem,
        string destination,
        string eventType,
        string consumerRole,
        int deliveryAttempt,
        Guid messageId,
        string correlationId,
        string? traceParent,
        string? traceState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messagingSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (deliveryAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(deliveryAttempt));
        }

        var activity = ActivitySource.StartActivity(
            $"{eventType} process",
            ActivityKind.Consumer,
            ParseParent(traceParent, traceState));
        SetMessagingTags(
            activity,
            messagingSystem,
            destination,
            eventType,
            messageId,
            correlationId);
        activity?.SetTag("messaging.consumer.group.name", consumerRole);
        activity?.SetTag("messaging.delivery.attempt", deliveryAttempt);
        return activity;
    }

    public static MessageIdentity ContinueFromCurrentActivity(MessageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Activity.Current is { Id: not null } activity
            ? identity with
            {
                TraceParent = activity.Id,
                TraceState = activity.TraceStateString
            }
            : identity;
    }

    public static void RecordPublish(
        string eventType,
        string destination,
        string result,
        TimeSpan duration)
    {
        var tags = EventTags(eventType, destination, "publish", result);
        Published.Add(1, tags);
        ProcessingDuration.Record(duration.TotalMilliseconds, tags);
    }

    public static void RecordConsume(
        string eventType,
        string destination,
        string module,
        string result,
        TimeSpan duration)
    {
        var tags = EventTags(eventType, destination, "consume", result, module);
        Consumed.Add(1, tags);
        ProcessingDuration.Record(duration.TotalMilliseconds, tags);
    }

    public static void RecordOutboxBatch(string module, int pendingCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        if (pendingCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingCount));
        }

        OutboxPending.Record(
            pendingCount,
            new KeyValuePair<string, object?>("module", module));
    }

    public static void RecordOutboxPublish(string module, string eventType, string result)
    {
        var tags = new TagList
        {
            { "event.type", eventType },
            { "module", module },
            { "operation", "outbox.publish" },
            { "result", result }
        };
        OutboxPublishAttempts.Add(1, tags);
        if (!string.Equals(result, "success", StringComparison.Ordinal))
        {
            OutboxPublishFailures.Add(1, tags);
        }
    }

    public static void RecordInboxDuplicate(string module, string eventType)
    {
        var tags = new TagList
        {
            { "event.type", eventType },
            { "module", module },
            { "operation", "inbox.begin" },
            { "result", "duplicate" }
        };
        InboxDuplicates.Add(1, tags);
    }

    public static void RecordForwarded(
        string eventType,
        string destination,
        int retryLevel,
        bool deadLetter)
    {
        if (retryLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retryLevel));
        }

        var tags = new TagList
        {
            { "event.type", eventType },
            { "messaging.destination.name", destination },
            { "operation", deadLetter ? "deadletter.forward" : "retry.forward" },
            { "result", "success" },
            { "retry.level", retryLevel }
        };
        if (deadLetter)
        {
            DeadLetterForwarded.Add(1, tags);
        }
        else
        {
            RetryForwarded.Add(1, tags);
        }
    }

    private static ActivityContext ParseParent(string? traceParent, string? traceState)
    {
        if (traceParent is null)
        {
            if (traceState is not null)
            {
                throw new ArgumentException("Trace state requires a trace parent.");
            }

            return default;
        }

        if (!ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var parent))
        {
            throw new ArgumentException("Trace context is invalid.");
        }

        return parent;
    }

    private static void SetMessagingTags(
        Activity? activity,
        string messagingSystem,
        string destination,
        string eventType,
        Guid messageId,
        string correlationId)
    {
        activity?.SetTag("messaging.system", messagingSystem);
        activity?.SetTag("messaging.destination.name", destination);
        activity?.SetTag("event.type", eventType);
        activity?.SetTag("messaging.message.id", messageId.ToString("D"));
        activity?.SetTag("business.correlation.id", correlationId);
    }

    private static TagList EventTags(
        string eventType,
        string destination,
        string operation,
        string result,
        string? module = null)
    {
        var tags = new TagList
        {
            { "event.type", eventType },
            { "messaging.destination.name", destination },
            { "operation", operation },
            { "result", result }
        };
        if (module is not null)
        {
            tags.Add("module", module);
        }

        return tags;
    }
}
