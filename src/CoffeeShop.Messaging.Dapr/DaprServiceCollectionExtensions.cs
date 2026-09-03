using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoffeeShop.Messaging.Dapr;

public static class DaprServiceCollectionExtensions
{
    public static IServiceCollection AddDaprMessaging(
        this IServiceCollection services,
        Action<DaprMessagingOptions> configure)
    {
        services.AddOptions<DaprMessagingOptions>()
            .Configure(configure)
            .Validate(
                options => IsValidName(options.PubSubName),
                "Dapr pub/sub name must contain only letters, digits, periods, underscores, or hyphens.")
            .Validate(
                options => IsValidName(options.TopicPrefix),
                "Dapr topic prefix must contain only letters, digits, periods, underscores, or hyphens.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AppApiToken)
                    && options.AppApiToken.Length >= 16,
                "Dapr app API token must contain at least 16 characters.")
            .Validate(
                options => IsCanonicalHttpOrigin(options.SidecarHttpEndpoint),
                "Dapr sidecar endpoint must be a canonical absolute HTTP or HTTPS origin.")
            .Validate(
                options => IsCanonicalHttpOrigin(options.SidecarGrpcEndpoint),
                "Dapr sidecar gRPC endpoint must be a canonical absolute HTTP or HTTPS origin.")
            .ValidateOnStart();
        services.TryAddSingleton<IMessageIdentityAccessor, MessageIdentityAccessor>();
        services.TryAddSingleton<
            IIntegrationFailureClassifier,
            DefaultIntegrationFailureClassifier>();
        services.AddDaprClient((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<DaprMessagingOptions>>().Value;
            client.UseHttpEndpoint(options.SidecarHttpEndpoint);
            client.UseGrpcEndpoint(options.SidecarGrpcEndpoint);
        });
        services.AddSingleton<IDaprPubSubClient, DaprPubSubClient>();
        services.AddSingleton<IIntegrationEventPublisher, DaprIntegrationEventPublisher>();
        services.AddSingleton<DaprSubscriptionDispatcher>();
        return services;
    }

    private static bool IsValidName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or '-');

    private static bool IsCanonicalHttpOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme is "http" or "https"
        && string.IsNullOrEmpty(endpoint.UserInfo)
        && endpoint.AbsolutePath == "/"
        && string.IsNullOrEmpty(endpoint.Query)
        && string.IsNullOrEmpty(endpoint.Fragment);
}
