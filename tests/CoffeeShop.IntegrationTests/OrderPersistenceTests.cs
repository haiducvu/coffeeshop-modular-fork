using CoffeeShop.Application.Orders;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders;
using CoffeeShop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoffeeShop.IntegrationTests;

public sealed class OrderPersistenceTests(PostgreSqlFixture fixture)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Saves_and_reloads_an_order_with_line_items()
    {
        await using var dbContext = CoffeeShopDbContext.Create(fixture.ConnectionString);
        await dbContext.Database.MigrateAsync();
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.NewGuid(),
            [
                new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista),
                new ItemSelection(ItemType.Croissant, PreparationStation.Kitchen)
            ]);

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var reloaded = await dbContext.Orders
            .Include(x => x.LineItems)
            .SingleAsync(x => x.Id == order.Id);

        Assert.Equal(2, reloaded.LineItems.Count);
        Assert.Collection(
            reloaded.LineItems.OrderBy(x => x.Name),
            first => Assert.Equal("CAPPUCCINO", first.Name),
            second => Assert.Equal("CROISSANT", second.Name));
    }

    [Fact]
    public async Task Lists_only_orders_at_requested_location()
    {
        await using var dbContext = CoffeeShopDbContext.Create(fixture.ConnectionString);
        await dbContext.Database.MigrateAsync();
        var atlantaOrder = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.NewGuid(),
            [new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista)]);
        var raleighOrder = Order.Place(
            OrderSource.Counter,
            Location.Raleigh,
            Guid.NewGuid(),
            [new ItemSelection(ItemType.Croissant, PreparationStation.Kitchen)]);

        dbContext.Orders.AddRange(atlantaOrder, raleighOrder);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var repository = new EfOrderRepository(dbContext);
        var orders = await repository.ListAsync(
            new OrdersByLocationSpecification(Location.Raleigh),
            CancellationToken.None);

        var order = Assert.Single(orders);
        Assert.Equal(raleighOrder.Id, order.Id);
    }
}
