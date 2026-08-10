using Npgsql;
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

    public async Task ResetModuleSchemasAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DROP SCHEMA IF EXISTS counter, barista, kitchen CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
