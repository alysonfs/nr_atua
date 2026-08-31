using Microsoft.Extensions.Logging;
using Npgsql;

namespace Atua.Collector.Consumer;

/// <summary>
/// Repositório PostgreSQL do consumer de snapshots (ADR-023).
/// Gerencia upsert em work_orders, append em work_order_histories e
/// persistência do resume token em consumer_states, na mesma transação.
/// </summary>
public sealed class WorkOrderPgRepository(
    string connectionString,
    ILogger<WorkOrderPgRepository> logger)
{
    private readonly string _connectionString = connectionString;

    /// <summary>
    /// Processa um snapshot: upsert em work_orders, append condicional em
    /// work_order_histories e atualização de consumer_states.
    /// Tudo na mesma transação (ADR-023, seção 4.1).
    /// </summary>
    public async Task ProcessSnapshotAsync(
        Guid snapshotId,
        Guid tenantId,
        string providerId,
        string status,
        string resumeToken,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. Upsert em work_orders — retorna o id e o status anterior (RF-017.1)
            var (workOrderId, previousStatus, isNew) =
                await UpsertWorkOrderAsync(conn, tx, tenantId, providerId, status, cancellationToken);

            // 2. Append em work_order_histories apenas se status mudou (RF-017.2)
            if (isNew || !string.Equals(previousStatus, status, StringComparison.Ordinal))
            {
                await InsertWorkOrderHistoryAsync(conn, tx,
                    workOrderId, snapshotId, tenantId, providerId, status, cancellationToken);
            }

            // 3. Persiste resume token na mesma transação (ADR-023 seção 4.1)
            await UpsertConsumerStateAsync(conn, tx, resumeToken, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            logger.LogDebug(
                "[CONSUMER] Transação commitada. SnapshotId={SnapshotId} TenantId={TenantId} ProviderId={ProviderId} Status={Status} IsNew={IsNew}",
                snapshotId, tenantId, providerId, status, isNew);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Atualiza apenas o resume token (para eventos descartados — status ausente).
    /// </summary>
    public async Task AdvanceResumeTokenAsync(
        string resumeToken,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            await UpsertConsumerStateAsync(conn, tx, resumeToken, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Lê o resume token persistido, ou null se não houver.
    /// </summary>
    public async Task<string?> GetResumeTokenAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT resume_token FROM consumer_states WHERE consumer_id = @id";
        cmd.Parameters.AddWithValue("@id", ConsumerId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    // -------------------------------------------------------------------------
    // Internos
    // -------------------------------------------------------------------------

    private const string ConsumerId = "snapshot-to-work-order";

    /// <summary>
    /// UPSERT em work_orders. Retorna (id, statusAnterior, isNew).
    /// </summary>
    private static async Task<(Guid WorkOrderId, string? PreviousStatus, bool IsNew)> UpsertWorkOrderAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid tenantId,
        string providerId,
        string newStatus,
        CancellationToken cancellationToken)
    {
        // Primeiro tenta ler o estado atual
        await using var selectCmd = conn.CreateCommand();
        selectCmd.Transaction = tx;
        selectCmd.CommandText =
            "SELECT id, status FROM work_orders WHERE tenant_id = @tid AND provider_id = @pid";
        selectCmd.Parameters.AddWithValue("@tid", tenantId);
        selectCmd.Parameters.AddWithValue("@pid", providerId);

        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var existingId = reader.GetGuid(0);
            var existingStatus = reader.GetString(1);
            await reader.CloseAsync();

            // Atualiza: sempre updated_at; status se mudou (RF-017.1)
            await using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText =
                "UPDATE work_orders SET status = @status, updated_at = @now WHERE id = @id";
            updateCmd.Parameters.AddWithValue("@status", newStatus);
            updateCmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
            updateCmd.Parameters.AddWithValue("@id", existingId);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken);

            return (existingId, existingStatus, false);
        }

        await reader.CloseAsync();

        // Insere nova OS
        var newId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText =
            """
            INSERT INTO work_orders (id, tenant_id, provider_id, status, created_at, updated_at)
            VALUES (@id, @tid, @pid, @status, @now, @now)
            """;
        insertCmd.Parameters.AddWithValue("@id", newId);
        insertCmd.Parameters.AddWithValue("@tid", tenantId);
        insertCmd.Parameters.AddWithValue("@pid", providerId);
        insertCmd.Parameters.AddWithValue("@status", newStatus);
        insertCmd.Parameters.AddWithValue("@now", now);
        await insertCmd.ExecuteNonQueryAsync(cancellationToken);

        return (newId, null, true);
    }

    private static async Task InsertWorkOrderHistoryAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid workOrderId,
        Guid snapshotId,
        Guid tenantId,
        string providerId,
        string status,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO work_order_histories
                (id, work_order_id, work_order_snapshot_id, tenant_id, provider_id, status, created_at, updated_at)
            VALUES (@id, @woid, @snid, @tid, @pid, @status, @now, @now)
            """;
        cmd.Parameters.AddWithValue("@id", Guid.CreateVersion7());
        cmd.Parameters.AddWithValue("@woid", workOrderId);
        cmd.Parameters.AddWithValue("@snid", snapshotId);
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@pid", providerId);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@now", now);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertConsumerStateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string resumeToken,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO consumer_states (consumer_id, resume_token, updated_at)
            VALUES (@id, @token::jsonb, @now)
            ON CONFLICT (consumer_id) DO UPDATE
            SET resume_token = EXCLUDED.resume_token, updated_at = EXCLUDED.updated_at
            """;
        cmd.Parameters.AddWithValue("@id", ConsumerId);
        cmd.Parameters.AddWithValue("@token", resumeToken);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
