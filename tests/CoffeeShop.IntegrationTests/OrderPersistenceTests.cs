using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders;
using CoffeeShop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OrderPersistenceTests(PostgreSqlFixture fixture)
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
}
