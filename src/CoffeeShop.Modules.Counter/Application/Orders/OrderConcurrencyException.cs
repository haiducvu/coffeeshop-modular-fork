namespace CoffeeShop.Modules.Counter.Application.Orders;

internal sealed class OrderConcurrencyException(string message, Exception innerException)
    : Exception(message, innerException);
