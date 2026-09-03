namespace CoffeeShop.Messaging.Dapr;

public sealed class DaprMessagingOptions
{
    public const string SectionName = "Messaging:Dapr";

    public string PubSubName { get; set; } = "coffeeshop-pubsub";
    public string TopicPrefix { get; set; } = "coffeeshop";
    public string AppApiToken { get; set; } = string.Empty;
    public string SidecarHttpEndpoint { get; set; } = "http://127.0.0.1:3500";
    public string SidecarGrpcEndpoint { get; set; } = "http://127.0.0.1:50001";
}
