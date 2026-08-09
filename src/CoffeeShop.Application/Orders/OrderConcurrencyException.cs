namespace CoffeeShop.Application.Orders;

public sealed class OrderConcurrencyException(string message, Exception innerException)
    : Exception(message, innerException);
