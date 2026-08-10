using CoffeeShop.Contracts.Menu;
using CoffeeShop.Modules.Counter.Application.Orders.GetFulfilled;
using CoffeeShop.Modules.Counter.Domain.Orders;

namespace CoffeeShop.ApplicationTests;

public sealed class GetFulfilledOrdersHandlerTests
{
    [Fact]
    public async Task Handle_maps_the_repository_result_to_a_read_model()
    {
        var repository = new RecordingOrderRepository();
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            [new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista)]);
        repository.Orders.Add(order);
        var handler = new GetFulfilledOrdersHandler(repository);

        var result = await handler.HandleAsync(CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal(order.Id, dto.Id);
        Assert.Equal("CAPPUCCINO", Assert.Single(dto.LineItems).Name);
    }
}
