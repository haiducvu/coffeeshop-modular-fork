using Xunit;
using Testcontainers.PostgreSql;

namespace CoffeeShop.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("coffeeshop_tests")
        .WithUsername("coffeeshop")
        .WithPassword("coffeeshop_tests_only")
        .Build();

    public string ConnectionString => _container.GetConnectionString();
    
    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
