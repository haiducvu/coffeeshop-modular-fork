using CoffeeShop.Application.Orders;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders;
using CoffeeShop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OrderConcurrencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Rejects_a_stale_completion_instead_of_losing_the_first_update()
    {
        await using var setup = CoffeeShopDbContext.Create(fixture.ConnectionString);
        await setup.Database.MigrateAsync();
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.NewGuid(),
            [
                new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista),
                new ItemSelection(ItemType.Croissant, PreparationStation.Kitchen)
            ]);
        setup.Orders.Add(order);
        await setup.SaveChangesAsync();

        await using var firstContext = CoffeeShopDbContext.Create(fixture.ConnectionString);
        await using var staleContext = CoffeeShopDbContext.Create(fixture.ConnectionString);
        var firstOrder = await firstContext.Orders
            .Include(value => value.LineItems)
            .SingleAsync(value => value.Id == order.Id);
        var staleOrder = await staleContext.Orders
            .Include(value => value.LineItems)
            .SingleAsync(value => value.Id == order.Id);
        firstOrder.CompleteItem(
            firstOrder.LineItems.Single(item => item.Station == PreparationStation.Barista).Id,
            "barista",
            DateTimeOffset.UnixEpoch);
        staleOrder.CompleteItem(
            staleOrder.LineItems.Single(item => item.Station == PreparationStation.Kitchen).Id,
            "kitchen",
            DateTimeOffset.UnixEpoch);
        var firstRepository = new EfOrderRepository(firstContext, new NoOpDomainEventDispatcher());
        var staleRepository = new EfOrderRepository(staleContext, new NoOpDomainEventDispatcher());

        await firstRepository.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<OrderConcurrencyException>(() =>
            staleRepository.SaveChangesAsync(CancellationToken.None));
    }
}
