namespace CoffeeShop.Messaging.Dapr;

internal enum DaprDeliveryResult
{
    Success,
    Retry,
    Drop
}
