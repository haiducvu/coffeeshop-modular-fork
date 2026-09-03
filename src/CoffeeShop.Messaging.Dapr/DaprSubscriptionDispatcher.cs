using System.Diagnostics;
using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Dapr;

internal sealed class DaprSubscriptionDispatcher(
    IServiceScopeFactory scopeFactory,
    IMessageIdentityAccessor identityAccessor,
    IIntegrationFailureClassifier failureClassifier,
    IOptions<DaprMessagingOptions> options,
    ILogger<DaprSubscriptionDispatcher> logger)
{
    private readonly DaprMessagingOptions _options = options.Value;

    internal async Task<DaprDeliveryResult> DispatchAsync<TPayload>(
        IntegrationEventEnvelope<TPayload> message,
        IReadOnlyList<string> consumerRoles,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(consumerRoles);
        if (consumerRoles.Count == 0)
        {
            throw new ArgumentException(
                "At least one Dapr consumer role is required.",
                nameof(consumerRoles));
        }

        var topic = IntegrationEventTopicResolver.Resolve<TPayload>(_options.TopicPrefix);
        var parent = Activity.Current;
        using var activity = MessagingTelemetry.StartConsumerActivity(
            "dapr",
            topic,
            message.EventType,
            "coffeeshop-api",
            deliveryAttempt: 1,
            message.MessageId,
            message.CorrelationId,
            parent?.Id,
            parent?.TraceStateString);
        using var identityScope = identityAccessor.Push(
            MessagingTelemetry.ContinueFromCurrentActivity(new MessageIdentity(
                message.CorrelationId,
                message.MessageId.ToString("D"),
                parent?.Id,
                parent?.TraceStateString)));
        var source = $"dapr:{_options.PubSubName}:{topic}";

        try
        {
            ValidateEnvelope(message);
            var aggregateResult = DaprDeliveryResult.Success;
            foreach (var consumerRole in consumerRoles)
            {
                var startedAt = Stopwatch.GetTimestamp();
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredKeyedService<
                        IIntegrationEventHandler<TPayload>>(consumerRole);
                    await handler.HandleAsync(
                        message,
                        new IntegrationMessageContext(
                            consumerRole,
                            source,
                            DeliveryAttempt: 1),
                        cancellationToken);
                    MessagingTelemetry.RecordConsume(
                        message.EventType,
                        topic,
                        consumerRole,
                        "success",
                        Stopwatch.GetElapsedTime(startedAt));
                }
                catch (OperationCanceledException)
                {
                    MessagingTelemetry.RecordConsume(
                        message.EventType,
                        topic,
                        consumerRole,
                        "cancelled",
                        Stopwatch.GetElapsedTime(startedAt));
                    throw;
                }
                catch (Exception exception)
                {
                    MessagingTelemetry.RecordConsume(
                        message.EventType,
                        topic,
                        consumerRole,
                        "failure",
                        Stopwatch.GetElapsedTime(startedAt));
                    aggregateResult = Aggregate(
                        aggregateResult,
                        Classify(exception, activity, consumerRole));
                }
            }

            if (aggregateResult == DaprDeliveryResult.Success)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
            }

            return aggregateResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Classify(exception, activity, "contract");
        }
    }

    private static DaprDeliveryResult Aggregate(
        DaprDeliveryResult current,
        DaprDeliveryResult next)
    {
        if (current == DaprDeliveryResult.Retry
            || next == DaprDeliveryResult.Retry)
        {
            return DaprDeliveryResult.Retry;
        }

        return current == DaprDeliveryResult.Drop
            || next == DaprDeliveryResult.Drop
                ? DaprDeliveryResult.Drop
                : DaprDeliveryResult.Success;
    }

    private DaprDeliveryResult Classify(
        Exception exception,
        Activity? activity,
        string consumerRole)
    {
        var failure = failureClassifier.Classify(exception);
        activity?.SetStatus(ActivityStatusCode.Error, failure.SafeErrorCode);
        logger.LogWarning(
            "Dapr delivery for {ConsumerRole} returned {FailureKind} with {ErrorCode}.",
            consumerRole,
            failure.Kind,
            failure.SafeErrorCode);
        return failure.Kind == IntegrationFailureKind.Permanent
            ? DaprDeliveryResult.Drop
            : DaprDeliveryResult.Retry;
    }

    private static void ValidateEnvelope<TPayload>(
        IntegrationEventEnvelope<TPayload> message)
        where TPayload : IIntegrationEvent
    {
        if (!string.Equals(
                message.EventType,
                TPayload.EventType,
                StringComparison.Ordinal)
            || message.EventVersion != TPayload.EventVersion)
        {
            throw new JsonException(
                "Dapr envelope metadata does not match the subscription contract.");
        }
    }
}
