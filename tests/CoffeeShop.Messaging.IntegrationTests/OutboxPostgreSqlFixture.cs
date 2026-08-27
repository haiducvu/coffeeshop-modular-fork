using Npgsql;
using Testcontainers.PostgreSql;

namespace CoffeeShop.Messaging.IntegrationTests;

public sealed class OutboxPostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("coffeeshop_outbox_tests")
            .WithUsername("coffeeshop")
            .WithPassword("coffeeshop_tests_only")
            .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS counter CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
