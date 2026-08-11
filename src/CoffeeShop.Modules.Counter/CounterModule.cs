using CoffeeShop.Modules.Counter.Application.GetOrder;
using CoffeeShop.Modules.Counter.Application.Orders.GetFulfilled;
using CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;
using FluentValidation;

namespace CoffeeShop.Modules.Counter;

public interface ICounterModule
{
    Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderInput input,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FulfilledOrder>> GetFulfilledOrdersAsync(
        CancellationToken cancellationToken);

    Task<OrderDetails?> GetOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken);
}

public sealed record PlaceOrderInput(
    int OrderSource,
    int Location,
    Guid LoyaltyMemberId,
    IReadOnlyList<int> BaristaItems,
    IReadOnlyList<int> KitchenItems);

public sealed record PlaceOrderResult(Guid OrderId);

public sealed record FulfilledOrder(
    Guid Id,
    Guid LoyaltyMemberId,
    string Status,
    IReadOnlyList<FulfilledOrderLineItem> LineItems);

public sealed record FulfilledOrderLineItem(
    Guid Id,
    string Name,
    decimal Price,
    string Station,
    string Status);

internal sealed class CounterModule(
    IValidator<PlaceOrderInput> validator,
    PlaceOrderHandler placeOrderHandler,
    GetFulfilledOrdersHandler getFulfilledOrdersHandler,
    GetOrderHandler getOrderHandler) : ICounterModule
{
    public async Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderInput input,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(input, cancellationToken);
        return await placeOrderHandler.HandleAsync(input, cancellationToken);
    }

    public Task<IReadOnlyList<FulfilledOrder>> GetFulfilledOrdersAsync(
        CancellationToken cancellationToken) =>
        getFulfilledOrdersHandler.HandleAsync(cancellationToken);

    public Task<OrderDetails?> GetOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken) =>
        getOrderHandler.HandleAsync(orderId, cancellationToken);
}
