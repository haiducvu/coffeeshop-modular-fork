using MediatR;

namespace CoffeeShop.Application.Orders.GetFulfilled;

public sealed record GetFulfilledOrdersQuery
    : IRequest<IReadOnlyList<FulfilledOrderDto>>;
