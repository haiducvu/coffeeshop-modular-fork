namespace CoffeeShop.Messaging.Abstractions;

public sealed record IntegrationMessageContext(
    string ConsumerRole,
    string Source,
    int DeliveryAttempt);
