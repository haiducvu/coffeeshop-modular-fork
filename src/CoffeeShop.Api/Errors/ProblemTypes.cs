using Microsoft.AspNetCore.Mvc;

namespace CoffeeShop.Api.Errors;

public static class ProblemTypes
{
    public const string Validation = "/problems/validation";
    public const string OrderNotFound = "/problems/order-not-found";
    public const string OrderConflict = "/problems/order-conflict";
    public const string Internal = "/problems/internal";

    public const string ValidationTitle = "Validation failed.";
    public const string OrderNotFoundTitle = "Order not found.";
    public const string OrderConflictTitle = "Order conflict.";
    public const string InternalTitle = "An unexpected error occurred.";

    public static ProblemDetails Create(string type, string title, int status) => new()
    {
        Type = type,
        Title = title,
        Status = status
    };
}
