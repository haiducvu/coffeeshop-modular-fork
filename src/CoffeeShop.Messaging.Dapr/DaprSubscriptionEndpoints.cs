using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Dapr;

public static class DaprSubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapDaprSubscriptionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<DaprMessagingOptions>>()
            .Value;
        endpoints.MapSubscribeHandler();
        endpoints.MapPost(
                "/dapr/orders/v1",
                (HttpRequest request,
                    DaprSubscriptionDispatcher dispatcher,
                    IOptions<JsonOptions> jsonOptions,
                    CancellationToken cancellationToken) => DispatchAsync<OrderPlacedV1>(
                        request,
                        dispatcher,
                        jsonOptions.Value.SerializerOptions,
                        ["barista", "kitchen"],
                        cancellationToken))
            .WithTopic(
                options.PubSubName,
                IntegrationEventTopicResolver.Resolve<OrderPlacedV1>(
                    options.TopicPrefix));
        endpoints.MapPost(
                "/dapr/preparation/v1",
                (HttpRequest request,
                    DaprSubscriptionDispatcher dispatcher,
                    IOptions<JsonOptions> jsonOptions,
                    CancellationToken cancellationToken) => DispatchAsync<OrderItemPreparedV1>(
                        request,
                        dispatcher,
                        jsonOptions.Value.SerializerOptions,
                        ["counter"],
                        cancellationToken))
            .WithTopic(
                options.PubSubName,
                IntegrationEventTopicResolver.Resolve<OrderItemPreparedV1>(
                    options.TopicPrefix));
        return endpoints;
    }

    private static async Task<IResult> DispatchAsync<TPayload>(
        HttpRequest request,
        DaprSubscriptionDispatcher dispatcher,
        JsonSerializerOptions serializerOptions,
        IReadOnlyList<string> consumerRoles,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        try
        {
            var message = await request.ReadFromJsonAsync<
                IntegrationEventEnvelope<TPayload>>(
                serializerOptions,
                cancellationToken);
            return message is null
                ? Drop()
                : await RespondAsync(dispatcher.DispatchAsync(
                    message,
                    consumerRoles,
                    cancellationToken));
        }
        catch (JsonException)
        {
            return Drop();
        }
    }

    private static async Task<IResult> RespondAsync(
        Task<DaprDeliveryResult> delivery)
    {
        var result = await delivery;
        return Results.Json(new DaprSubscriptionResponse(result switch
        {
            DaprDeliveryResult.Success => "SUCCESS",
            DaprDeliveryResult.Retry => "RETRY",
            DaprDeliveryResult.Drop => "DROP",
            _ => throw new InvalidOperationException("Unknown Dapr delivery result.")
        }));
    }

    private static IResult Drop() =>
        Results.Json(new DaprSubscriptionResponse("DROP"));

    private sealed record DaprSubscriptionResponse(string Status);
}
