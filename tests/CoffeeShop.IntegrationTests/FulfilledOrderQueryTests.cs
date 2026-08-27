using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Counter.Application.Orders;
using CoffeeShop.Modules.Counter.Domain.Orders;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class FulfilledOrderQueryTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Lists_only_fulfilled_orders_and_includes_line_items()
    {
        await fixture.ResetModuleSchemasAsync();
        await using var dbContext = CounterDbContext.Create(fixture.ConnectionString);
        await dbContext.Database.MigrateAsync();
        var fulfilled = CreateOrder(ItemType.Cappuccino, PreparationStation.Barista);
        var inProgress = CreateOrder(ItemType.Croissant, PreparationStation.Kitchen);
        dbContext.Orders.AddRange(fulfilled, inProgress);
        dbContext.Entry(fulfilled).Property(x => x.Status).CurrentValue = OrderStatus.Fulfilled;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var repository = new EfOrderRepository(dbContext, new NoOpDomainEventDispatcher());

        var orders = await repository.ListAsync(
            new FulfilledOrdersSpecification(),
            CancellationToken.None);

        var order = Assert.Single(orders);
        Assert.Equal(fulfilled.Id, order.Id);
        Assert.Single(order.LineItems);
    }

    private static Order CreateOrder(ItemType itemType, PreparationStation station) =>
        Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.NewGuid(),
            [new ItemSelection(itemType, station)]);
}
