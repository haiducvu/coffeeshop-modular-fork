namespace CoffeeShop.Api.Errors;

public sealed class OrderNotFoundException(Guid orderId)
    : Exception($"Order '{orderId}' was not found.");

public sealed class OrderConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);
