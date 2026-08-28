using System.Data;
using CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;

internal sealed class KitchenOutboxStore(KitchenDbContext dbContext)
    : IKitchenOutboxStore
{
    private const string InvalidContract = "invalid-contract";
    private const string PublishFailed = "publish-failed";

    public async Task<IReadOnlyList<ClaimedKitchenOutboxMessage>> ClaimBatchAsync(
        Guid leaseId,
        int batchSize,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            WITH candidates AS (
                SELECT "MessageId"
                FROM kitchen.outbox_messages
                WHERE "PublishedAtUtc" IS NULL
                  AND "RejectedAtUtc" IS NULL
                  AND "NextAttemptAtUtc" <= @now
                  AND ("LeaseExpiresAtUtc" IS NULL OR "LeaseExpiresAtUtc" <= @now)
                ORDER BY "NextAttemptAtUtc", "OccurredAtUtc", "MessageId"
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE kitchen.outbox_messages AS message
            SET "LeaseId" = @leaseId,
                "LeaseExpiresAtUtc" = @leaseExpiresAt
            FROM candidates
            WHERE message."MessageId" = candidates."MessageId"
            RETURNING message."MessageId",
                      message."EventType",
                      message."EventVersion",
                      message."EnvelopeJson"::text,
                      message."CorrelationId",
                      message."CausationId",
                      message."TraceParent",
                      message."TraceState";
            """;
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("leaseId", NpgsqlDbType.Uuid, leaseId);
            command.Parameters.AddWithValue("batchSize", NpgsqlDbType.Integer, batchSize);
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            command.Parameters.AddWithValue(
                "leaseExpiresAt",
                NpgsqlDbType.TimestampTz,
                leaseExpiresAt);
            var claimed = new List<ClaimedKitchenOutboxMessage>(batchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(new ClaimedKitchenOutboxMessage(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }

            return claimed;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public Task MarkPublishedAsync(
        Guid messageId,
        Guid leaseId,
        DateTimeOffset now,
        CancellationToken cancellationToken) => ExecuteAsync(
            """
            UPDATE kitchen.outbox_messages
            SET "PublishedAtUtc" = @timestamp,
                "LeaseId" = NULL,
                "LeaseExpiresAtUtc" = NULL,
                "LastErrorCode" = NULL
            WHERE "MessageId" = @messageId
              AND "LeaseId" = @leaseId
              AND "PublishedAtUtc" IS NULL
              AND "RejectedAtUtc" IS NULL;
            """,
            messageId,
            leaseId,
            now,
            cancellationToken);

    public Task MarkFailedAsync(
        Guid messageId,
        Guid leaseId,
        string safeErrorCode,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(safeErrorCode, PublishFailed, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unsafe Outbox error code.", nameof(safeErrorCode));
        }

        return ExecuteAsync(
            """
            UPDATE kitchen.outbox_messages
            SET "Attempts" = "Attempts" + 1,
                "NextAttemptAtUtc" = @timestamp,
                "LeaseId" = NULL,
                "LeaseExpiresAtUtc" = NULL,
                "LastErrorCode" = @safeErrorCode
            WHERE "MessageId" = @messageId
              AND "LeaseId" = @leaseId
              AND "PublishedAtUtc" IS NULL
              AND "RejectedAtUtc" IS NULL;
            """,
            messageId,
            leaseId,
            nextAttemptAt,
            cancellationToken,
            safeErrorCode);
    }

    public Task MarkRejectedAsync(
        Guid messageId,
        Guid leaseId,
        string safeErrorCode,
        DateTimeOffset rejectedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(safeErrorCode, InvalidContract, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The outbox rejection code is not on the safe allow-list.",
                nameof(safeErrorCode));
        }

        return ExecuteAsync(
            """
            UPDATE kitchen.outbox_messages
            SET "RejectedAtUtc" = @timestamp,
                "LeaseId" = NULL,
                "LeaseExpiresAtUtc" = NULL,
                "LastErrorCode" = @safeErrorCode
            WHERE "MessageId" = @messageId
              AND "LeaseId" = @leaseId
              AND "PublishedAtUtc" IS NULL
              AND "RejectedAtUtc" IS NULL;
            """,
            messageId,
            leaseId,
            rejectedAtUtc,
            cancellationToken,
            safeErrorCode);
    }

    private async Task ExecuteAsync(
        string sql,
        Guid messageId,
        Guid leaseId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken,
        string? safeErrorCode = null)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("messageId", NpgsqlDbType.Uuid, messageId);
            command.Parameters.AddWithValue("leaseId", NpgsqlDbType.Uuid, leaseId);
            command.Parameters.AddWithValue("timestamp", NpgsqlDbType.TimestampTz, timestamp);
            if (safeErrorCode is not null)
            {
                command.Parameters.AddWithValue(
                    "safeErrorCode",
                    NpgsqlDbType.Text,
                    safeErrorCode);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
