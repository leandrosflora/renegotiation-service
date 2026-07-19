using Microsoft.Extensions.Options;
using Npgsql;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Configuration;

namespace renegotiation_service.Adapters.Outbound.Persistence;

public sealed class PostgresSimulationIdempotencyStore(
    NpgsqlDataSource dataSource,
    IOptions<PostgresOptions> options) : ISimulationIdempotencyStore
{
    private const string Operation = "simulation";
    private readonly int _leaseSeconds = Math.Max(30, options.Value.IdempotencyLeaseSeconds);
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaReady;

    public async Task<IdempotencyLease> TryAcquireAsync(
        Guid tenantId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string acquireSql = """
            INSERT INTO ops.renegotiation_idempotency
                (tenant_id, operation, idempotency_key, request_hash, status, lease_until,
                 attempt_count, created_at, updated_at)
            VALUES
                (@tenant_id, @operation, @idempotency_key, @request_hash, 'processing',
                 now() + make_interval(secs => @lease_seconds), 1, now(), now())
            ON CONFLICT (tenant_id, operation, idempotency_key) DO NOTHING
            RETURNING status;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(acquireSql, connection, transaction))
        {
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("operation", Operation);
            command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
            command.Parameters.AddWithValue("request_hash", requestHash);
            command.Parameters.AddWithValue("lease_seconds", _leaseSeconds);
            var acquired = await command.ExecuteScalarAsync(cancellationToken);
            if (acquired is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new IdempotencyLease(IdempotencyAcquireStatus.Acquired);
            }
        }

        const string selectSql = """
            SELECT request_hash, status, response::text
            FROM ops.renegotiation_idempotency
            WHERE tenant_id = @tenant_id
              AND operation = @operation
              AND idempotency_key = @idempotency_key;
            """;
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        select.Parameters.AddWithValue("tenant_id", tenantId);
        select.Parameters.AddWithValue("operation", Operation);
        select.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Idempotency row could not be resolved.");
        }
        var existingHash = reader.GetString(0);
        var status = reader.GetString(1);
        var responseJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);

        if (!string.Equals(existingHash, requestHash, StringComparison.Ordinal))
        {
            return new IdempotencyLease(IdempotencyAcquireStatus.Conflict);
        }
        return status switch
        {
            "completed" => new IdempotencyLease(IdempotencyAcquireStatus.Completed, responseJson),
            "processing" => new IdempotencyLease(IdempotencyAcquireStatus.InProgress),
            "failed" => new IdempotencyLease(IdempotencyAcquireStatus.InProgress),
            _ => throw new InvalidOperationException($"Unsupported idempotency status '{status}'.")
        };
    }

    public async Task CompleteAsync(
        Guid tenantId,
        string idempotencyKey,
        string responseJson,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            UPDATE ops.renegotiation_idempotency
            SET status = 'completed', response = @response::jsonb, lease_until = NULL,
                completed_at = now(), last_error = NULL, updated_at = now()
            WHERE tenant_id = @tenant_id
              AND operation = @operation
              AND idempotency_key = @idempotency_key;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("operation", Operation);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("response", responseJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid tenantId,
        string idempotencyKey,
        string errorType,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            UPDATE ops.renegotiation_idempotency
            SET status = 'failed', lease_until = NULL, last_error = @last_error, updated_at = now()
            WHERE tenant_id = @tenant_id
              AND operation = @operation
              AND idempotency_key = @idempotency_key;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("operation", Operation);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("last_error", errorType.Length <= 500 ? errorType : errorType[..500]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }
        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
            {
                return;
            }
            const string sql = """
                CREATE SCHEMA IF NOT EXISTS ops;
                CREATE TABLE IF NOT EXISTS ops.renegotiation_idempotency (
                    tenant_id uuid NOT NULL,
                    operation text NOT NULL,
                    idempotency_key text NOT NULL,
                    request_hash text NOT NULL,
                    status text NOT NULL CHECK (status IN ('processing', 'completed', 'failed')),
                    response jsonb,
                    lease_until timestamptz,
                    attempt_count integer NOT NULL DEFAULT 0,
                    last_error text,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    updated_at timestamptz NOT NULL DEFAULT now(),
                    completed_at timestamptz,
                    PRIMARY KEY (tenant_id, operation, idempotency_key)
                );
                CREATE INDEX IF NOT EXISTS idx_renegotiation_idempotency_status_lease
                    ON ops.renegotiation_idempotency (status, lease_until);
                """;
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }
}
