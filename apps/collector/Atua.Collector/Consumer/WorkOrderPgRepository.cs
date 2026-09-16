using Microsoft.Extensions.Logging;
using Npgsql;

namespace Atua.Collector.Consumer;

/// <summary>
/// Repositório PostgreSQL do consumer de <c>provider_interactions</c> (Fase 4 do refactor de
/// persistência — docs/implementation/PLANO-refactor-provider-interactions-collector.md).
/// Gerencia upsert em work_orders, append em work_order_histories e
/// persistência do resume token em consumer_states, na mesma transação.
/// Substitui o consumer de <c>work_order_snapshots</c> (ADR-023, superseded).
/// </summary>
public sealed class WorkOrderPgRepository(
    string connectionString,
    ILogger<WorkOrderPgRepository> logger)
{
    private readonly string _connectionString = connectionString;

    /// <summary>
    /// Processa um documento inteiro de <c>provider_interactions</c> (Fase 4 do refactor de
    /// persistência): N upserts em <c>work_orders</c> + N appends condicionais em
    /// <c>work_order_histories</c> (um por OS presente em <paramref name="orders"/>) + 1
    /// atualização de <c>consumer_states</c> — tudo na mesma transação. Diferente do consumer
    /// antigo (1 OS por documento, modelo <c>work_order_snapshots</c>, superseded), aqui uma
    /// única interação pode conter várias OS (ex.: uma página de <c>list_query</c>).
    /// </summary>
    public async Task ProcessInteractionAsync(
        Guid interactionId,
        Guid tenantId,
        IReadOnlyList<ProviderWorkOrderData> orders,
        string resumeToken,
        string consumerId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var order in orders)
            {
                var (workOrderId, previousStatus, isNew) =
                    await UpsertWorkOrderAsync(conn, tx, tenantId, order, cancellationToken);

                if (isNew || !string.Equals(previousStatus, order.Status, StringComparison.Ordinal))
                {
                    await InsertWorkOrderHistoryAsync(conn, tx,
                        workOrderId, interactionId, tenantId, order, cancellationToken);
                }
            }

            await UpsertConsumerStateAsync(conn, tx, consumerId, resumeToken, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            logger.LogDebug(
                "[CONSUMER] Interação processada. InteractionId={InteractionId} TenantId={TenantId} OrdersCount={OrdersCount}",
                interactionId, tenantId, orders.Count);
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
        string consumerId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            await UpsertConsumerStateAsync(conn, tx, consumerId, resumeToken, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Lê o resume token persistido para <paramref name="consumerId"/>, ou null se não
    /// houver.
    /// </summary>
    public async Task<string?> GetResumeTokenAsync(
        string consumerId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        // Nomes de coluna entre aspas em PascalCase: o EF Core cria as colunas dessa
        // forma (case-sensitive) mesmo com a tabela em snake_case (ver AtuaDbContext,
        // sem HasColumnName em nenhuma entidade — convenção do projeto inteiro).
        cmd.CommandText = "SELECT \"ResumeToken\" FROM consumer_states WHERE \"ConsumerId\" = @id";
        cmd.Parameters.AddWithValue("@id", consumerId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    /// <summary>
    /// Limpa (define como <c>NULL</c>) o resume token persistido de <paramref name="consumerId"/>.
    /// Usado quando o token não pode mais ser retomado — o Mongo Atlas já reciclou o oplog
    /// que cobriria aquele ponto (erro <c>ChangeStreamHistoryLost</c>/código 286) — forçando
    /// o próximo <c>WatchAsync</c> a abrir o stream sem <c>ResumeAfter</c>, a partir do ponto
    /// corrente. Não apaga nenhum dado de negócio, apenas o estado operacional do consumer.
    /// </summary>
    public async Task ClearResumeTokenAsync(
        string consumerId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO consumer_states ("ConsumerId", "ResumeToken", "UpdatedAt")
            VALUES (@id, NULL, @now)
            ON CONFLICT ("ConsumerId") DO UPDATE
            SET "ResumeToken" = NULL, "UpdatedAt" = EXCLUDED."UpdatedAt"
            """;
        cmd.Parameters.AddWithValue("@id", consumerId);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Internos
    // -------------------------------------------------------------------------

    /// <summary>
    /// Colunas descritivas (datas do provedor + consumidor/contato/endereço/produto/sintoma)
    /// compartilhadas entre o UPDATE/INSERT de <c>work_orders</c> e o INSERT de
    /// <c>work_order_histories</c> — evita duplicar a lista em quatro lugares.
    /// </summary>
    private static readonly string[] DetailColumns =
    [
        "ProviderCreatedAt", "ProviderUpdatedAt",
        "CustomerType", "CustomerName", "CustomerCpf",
        "ContactEmail", "ContactPhone", "ContactName",
        "Address", "ZipCode", "CountryName", "StateName", "CityName",
        "ProductBrand", "PdCode", "CategoryId", "ProductCategoryCode", "ProductCode",
        "ProductModel", "ProductStatus", "Symptom",
    ];

    /// <summary>
    /// Adiciona os parâmetros <c>@d0..@d20</c> correspondentes a <see cref="DetailColumns"/>,
    /// na mesma ordem, convertendo null para <see cref="DBNull"/>.
    /// </summary>
    private static void AddDetailParameters(NpgsqlCommand cmd, ProviderWorkOrderData order)
    {
        object?[] values =
        [
            order.ProviderCreatedAt, order.ProviderUpdatedAt,
            order.CustomerType, order.CustomerName, order.CustomerCpf,
            order.ContactEmail, order.ContactPhone, order.ContactName,
            order.Address, order.ZipCode, order.CountryName, order.StateName, order.CityName,
            order.ProductBrand, order.PdCode, order.CategoryId, order.ProductCategoryCode, order.ProductCode,
            order.ProductModel, order.ProductStatus, order.Symptom,
        ];

        for (var i = 0; i < values.Length; i++)
            cmd.Parameters.AddWithValue($"@d{i}", values[i] ?? (object)DBNull.Value);
    }

    private static string DetailColumnList() => string.Join(", ", DetailColumns.Select(c => $"\"{c}\""));

    private static string DetailParamList() => string.Join(", ", Enumerable.Range(0, DetailColumns.Length).Select(i => $"@d{i}"));

    private static string DetailAssignmentList() =>
        string.Join(", ", DetailColumns.Select((c, i) => $"\"{c}\" = @d{i}"));

    /// <summary>
    /// UPSERT em work_orders. Retorna (id, statusAnterior, isNew).
    /// </summary>
    private static async Task<(Guid WorkOrderId, string? PreviousStatus, bool IsNew)> UpsertWorkOrderAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid tenantId,
        ProviderWorkOrderData order,
        CancellationToken cancellationToken)
    {
        var providerId = order.ProviderId;
        var newStatus = order.Status;

        // Primeiro tenta ler o estado atual
        await using var selectCmd = conn.CreateCommand();
        selectCmd.Transaction = tx;
        selectCmd.CommandText =
            "SELECT \"Id\", \"Status\" FROM work_orders WHERE \"TenantId\" = @tid AND \"ProviderId\" = @pid";
        selectCmd.Parameters.AddWithValue("@tid", tenantId);
        selectCmd.Parameters.AddWithValue("@pid", providerId);

        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var existingId = reader.GetGuid(0);
            var existingStatus = reader.GetString(1);
            await reader.CloseAsync();

            // Atualiza: sempre updated_at + campos descritivos; status se mudou (RF-017.1)
            await using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText =
                $"UPDATE work_orders SET \"Status\" = @status, \"UpdatedAt\" = @now, {DetailAssignmentList()} WHERE \"Id\" = @id";
            updateCmd.Parameters.AddWithValue("@status", newStatus);
            updateCmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
            updateCmd.Parameters.AddWithValue("@id", existingId);
            AddDetailParameters(updateCmd, order);
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
            $"""
            INSERT INTO work_orders ("Id", "TenantId", "ProviderId", "Status", "CreatedAt", "UpdatedAt", {DetailColumnList()})
            VALUES (@id, @tid, @pid, @status, @now, @now, {DetailParamList()})
            """;
        insertCmd.Parameters.AddWithValue("@id", newId);
        insertCmd.Parameters.AddWithValue("@tid", tenantId);
        insertCmd.Parameters.AddWithValue("@pid", providerId);
        insertCmd.Parameters.AddWithValue("@status", newStatus);
        insertCmd.Parameters.AddWithValue("@now", now);
        AddDetailParameters(insertCmd, order);
        await insertCmd.ExecuteNonQueryAsync(cancellationToken);

        return (newId, null, true);
    }

    /// <summary>
    /// <paramref name="interactionId"/> ocupa a coluna <c>WorkOrderSnapshotId</c> — mantida
    /// sem renomear (referência de aplicação, sem FK de banco) — agora com o <c>_id</c> do
    /// documento de <c>provider_interactions</c> que originou esta OS, não mais o de um
    /// <c>work_order_snapshots</c> (superseded).
    /// </summary>
    private static async Task InsertWorkOrderHistoryAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid workOrderId,
        Guid interactionId,
        Guid tenantId,
        ProviderWorkOrderData order,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            $"""
            INSERT INTO work_order_histories
                ("Id", "WorkOrderId", "WorkOrderSnapshotId", "TenantId", "ProviderId", "Status", "CreatedAt", "UpdatedAt", {DetailColumnList()})
            VALUES (@id, @woid, @snid, @tid, @pid, @status, @now, @now, {DetailParamList()})
            """;
        cmd.Parameters.AddWithValue("@id", Guid.CreateVersion7());
        cmd.Parameters.AddWithValue("@woid", workOrderId);
        cmd.Parameters.AddWithValue("@snid", interactionId);
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@pid", order.ProviderId);
        cmd.Parameters.AddWithValue("@status", order.Status);
        cmd.Parameters.AddWithValue("@now", now);
        AddDetailParameters(cmd, order);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertConsumerStateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string consumerId,
        string resumeToken,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO consumer_states ("ConsumerId", "ResumeToken", "UpdatedAt")
            VALUES (@id, @token::jsonb, @now)
            ON CONFLICT ("ConsumerId") DO UPDATE
            SET "ResumeToken" = EXCLUDED."ResumeToken", "UpdatedAt" = EXCLUDED."UpdatedAt"
            """;
        cmd.Parameters.AddWithValue("@id", consumerId);
        cmd.Parameters.AddWithValue("@token", resumeToken);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
