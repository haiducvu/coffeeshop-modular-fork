using Microsoft.EntityFrameworkCore.Design;

namespace CoffeeShop.Modules.Counter.Infrastructure.Persistence;

internal sealed class CounterDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<CounterDbContext>
{
    public CounterDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CoffeeShop")
            ?? "Host=localhost;Port=5432;Database=coffeeshop;Username=coffeeshop;Password=local_only";
        return CounterDbContext.Create(connectionString);
    }
}
