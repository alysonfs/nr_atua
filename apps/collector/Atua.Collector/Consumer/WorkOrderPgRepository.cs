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
    : IWorkOrderPgRepository
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
        DateTimeOffset interactionCreatedAt,
        string interactionType,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            var integrationId = await ResolveIntegrationIdAsync(conn, tx, tenantId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Tenant {tenantId} não possui Integration cadastrada — IntegrationId é obrigatório em work_orders/work_order_histories.");

            foreach (var order in orders)
            {
                var (workOrderId, previousStatus, isNew) =
                    await UpsertWorkOrderAsync(
                        conn, tx, tenantId, integrationId, order, interactionType, interactionCreatedAt, cancellationToken);

                if (isNew || !string.Equals(previousStatus, order.Status, StringComparison.Ordinal))
                {
                    await InsertWorkOrderHistoryAsync(conn, tx,
                        workOrderId, interactionId, tenantId, integrationId, order, cancellationToken);
                }
            }

            await UpsertConsumerStateAsync(conn, tx, consumerId, resumeToken, interactionCreatedAt, cancellationToken);

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
        DateTimeOffset interactionCreatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            await UpsertConsumerStateAsync(conn, tx, consumerId, resumeToken, interactionCreatedAt, cancellationToken);
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
    /// Lê a marca d'água de <c>LastProcessedInteractionCreatedAt</c> persistida para
    /// <paramref name="consumerId"/> (ADR-030, decisão 2), usada pelo
    /// <c>ProviderInteractionCleanupJob</c> para apagar documentos já confirmados como
    /// processados no Postgres. Retorna <c>null</c> se não houver marca d'água ainda.
    /// </summary>
    public async Task<DateTimeOffset?> GetLastProcessedInteractionCreatedAtAsync(
        string consumerId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"LastProcessedInteractionCreatedAt\" FROM consumer_states WHERE \"ConsumerId\" = @id";
        cmd.Parameters.AddWithValue("@id", consumerId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is DateTime dt ? new DateTimeOffset(dt, TimeSpan.Zero) : null;
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
        "WorkOrderProviderNo", "ServiceRequestId", "Amount",
        "ProviderCreatedAt", "ProviderUpdatedAt",
        "CustomerType", "CustomerName", "CustomerCpf",
        "ContactEmail", "ContactPhone", "ContactName",
        "Address", "ZipCode", "CountryName", "StateName", "CityName",
        "ProductBrand", "PdCode", "CategoryId", "ProductCategoryCode", "ProductCode",
        "ProductModel", "ProductStatus", "Symptom",
    ];

    /// <summary>
    /// Subconjunto de <see cref="DetailColumns"/> com dados de contato do consumidor.
    /// Quando a interação processada é um <c>list_query</c> (sem <c>orderDetail</c>), o
    /// adapter cai no fallback de campos crus da listagem, que vêm MASCARADOS
    /// ("S****S", "5584****51") mas não-nulos — por isso o COALESCE por si só não protege
    /// esses campos, o valor mascarado venceria o COALESCE e sobrescreveria o dado bom já
    /// obtido por um detail_query anterior (bug encontrado em produção 2026-09-24). Essas
    /// colunas só podem ser atualizadas quando a própria interação é um detail_query.
    /// </summary>
    private static readonly HashSet<string> ContactDetailColumns =
    [
        "CustomerType", "CustomerName", "CustomerCpf",
        "ContactEmail", "ContactPhone", "ContactName",
        "Address", "ZipCode", "CountryName", "StateName", "CityName",
    ];

    /// <summary>
    /// Adiciona os parâmetros <c>@d0..@d20</c> correspondentes a <see cref="DetailColumns"/>,
    /// na mesma ordem, convertendo null para <see cref="DBNull"/>.
    /// </summary>
    private static void AddDetailParameters(NpgsqlCommand cmd, ProviderWorkOrderData order)
    {
        object?[] values =
        [
            order.WorkOrderProviderNo, order.ServiceRequestId, order.Amount,
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

    private static string DetailAssignmentList(bool isDetailQuery) =>
        // COALESCE preserva detalhe já capturado quando uma interação sem esses campos
        // (ex.: list_query puro, sem orderDetail) é processada depois (RF-026.4/ADR-031,
        // decisão 4) — evita zerar CustomerName/Address/etc. já obtidos por um detail_query
        // anterior. Colunas de contato (ContactDetailColumns) só entram no SET quando a
        // própria interação é um detail_query — do contrário o fallback mascarado do
        // list_query venceria o COALESCE e regravaria o dado mascarado por cima do bom.
        // O INSERT (OS nova) continua gravando os valores recebidos diretamente.
        string.Join(", ", DetailColumns.Select((c, i) =>
            !isDetailQuery && ContactDetailColumns.Contains(c)
                ? $"\"{c}\" = \"{c}\""
                : $"\"{c}\" = COALESCE(@d{i}, \"{c}\")"));

    /// <summary>
    /// Resolve o <c>IntegrationId</c> do tenant para popular a FK em work_orders/
    /// work_order_histories (traço até Tenant/Provider). No MVP existe exatamente uma
    /// integração (iService) por tenant (ADR-018), então basta a primeira encontrada.
    /// Retorna null se o tenant ainda não tiver integração — nesse caso o chamador deve
    /// falhar o processamento, pois a coluna é obrigatória.
    /// </summary>
    private static async Task<Guid?> ResolveIntegrationIdAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT \"Id\" FROM integrations WHERE \"TenantId\" = @tid LIMIT 1";
        cmd.Parameters.AddWithValue("@tid", tenantId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    /// <summary>
    /// UPSERT em work_orders. Retorna (id, statusAnterior, isNew).
    /// Também decide <c>NeedsDetailFetch</c>/<c>DetailsFetchedAt</c> (RF-026/ADR-031, "Decisão"
    /// item 2): OS nova ou com mudança de status (exceto terminal já enriquecida) precisa de
    /// detalhe; quando a própria interação processada é um <c>detail_query</c> bem-sucedido,
    /// marca a pendência como atendida incondicionalmente.
    /// </summary>
    private static async Task<(Guid WorkOrderId, string? PreviousStatus, bool IsNew)> UpsertWorkOrderAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid integrationId,
        ProviderWorkOrderData order,
        string interactionType,
        DateTimeOffset interactionCreatedAt,
        CancellationToken cancellationToken)
    {
        var workOrderProviderId = order.WorkOrderProviderId;
        var newStatus = order.Status;
        var isDetailQuery = string.Equals(interactionType, "detail_query", StringComparison.OrdinalIgnoreCase);

        // Primeiro tenta ler o estado atual
        await using var selectCmd = conn.CreateCommand();
        selectCmd.Transaction = tx;
        selectCmd.CommandText =
            "SELECT \"Id\", \"Status\", \"NeedsDetailFetch\", \"DetailsFetchedAt\" FROM work_orders " +
            "WHERE \"TenantId\" = @tid AND \"WorkOrderProviderId\" = @pid";
        selectCmd.Parameters.AddWithValue("@tid", tenantId);
        selectCmd.Parameters.AddWithValue("@pid", workOrderProviderId);

        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var existingId = reader.GetGuid(0);
            var existingStatus = reader.GetString(1);
            var existingNeedsDetailFetch = reader.GetBoolean(2);
            DateTimeOffset? existingDetailsFetchedAt = await reader.IsDBNullAsync(3, cancellationToken)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(3);
            await reader.CloseAsync();

            var statusChanged = !string.Equals(existingStatus, newStatus, StringComparison.Ordinal);

            // needsDetail := statusChanged AND NOT (existing.DetailsFetchedAt != null AND isTerminal(existingStatus))
            // Quando a fórmula é falsa (status não mudou, ou mudou mas já era terminal
            // enriquecida), NeedsDetailFetch não é tocado — preserva o valor atual.
            var needsDetailFetch = existingNeedsDetailFetch;
            if (statusChanged)
            {
                var wasTerminalAlreadyEnriched =
                    existingDetailsFetchedAt is not null && WorkOrderTerminalStatuses.IsTerminal(existingStatus);
                needsDetailFetch = !wasTerminalAlreadyEnriched;
            }

            var detailsFetchedAt = existingDetailsFetchedAt;
            if (isDetailQuery)
            {
                // Confirmação incondicional de que a pendência de detalhe foi atendida.
                detailsFetchedAt = interactionCreatedAt;
                needsDetailFetch = false;
            }

            // Atualiza: sempre updated_at + campos descritivos; status se mudou (RF-017.1)
            await using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText =
                "UPDATE work_orders SET \"Status\" = @status, \"UpdatedAt\" = @now, \"IntegrationId\" = @iid, " +
                $"\"NeedsDetailFetch\" = @needsDetailFetch, \"DetailsFetchedAt\" = @detailsFetchedAt, {DetailAssignmentList(isDetailQuery)} " +
                "WHERE \"Id\" = @id";
            updateCmd.Parameters.AddWithValue("@status", newStatus);
            updateCmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
            updateCmd.Parameters.AddWithValue("@iid", integrationId);
            updateCmd.Parameters.AddWithValue("@needsDetailFetch", needsDetailFetch);
            updateCmd.Parameters.AddWithValue("@detailsFetchedAt", (object?)detailsFetchedAt ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@id", existingId);
            AddDetailParameters(updateCmd, order);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken);

            return (existingId, existingStatus, false);
        }

        await reader.CloseAsync();

        // Insere nova OS — RF-026.1: sempre precisa de detalhe, exceto se a própria
        // interação que a criou já é o detail_query confirmando o enriquecimento.
        var newId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var newNeedsDetailFetch = !isDetailQuery;
        var newDetailsFetchedAt = isDetailQuery ? interactionCreatedAt : (DateTimeOffset?)null;

        await using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText =
            $"""
            INSERT INTO work_orders ("Id", "TenantId", "IntegrationId", "WorkOrderProviderId", "Status", "CreatedAt", "UpdatedAt", "NeedsDetailFetch", "DetailsFetchedAt", {DetailColumnList()})
            VALUES (@id, @tid, @iid, @pid, @status, @now, @now, @needsDetailFetch, @detailsFetchedAt, {DetailParamList()})
            """;
        insertCmd.Parameters.AddWithValue("@id", newId);
        insertCmd.Parameters.AddWithValue("@tid", tenantId);
        insertCmd.Parameters.AddWithValue("@iid", integrationId);
        insertCmd.Parameters.AddWithValue("@pid", workOrderProviderId);
        insertCmd.Parameters.AddWithValue("@status", newStatus);
        insertCmd.Parameters.AddWithValue("@now", now);
        insertCmd.Parameters.AddWithValue("@needsDetailFetch", newNeedsDetailFetch);
        insertCmd.Parameters.AddWithValue("@detailsFetchedAt", (object?)newDetailsFetchedAt ?? DBNull.Value);
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
        Guid integrationId,
        ProviderWorkOrderData order,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            $"""
            INSERT INTO work_order_histories
                ("Id", "WorkOrderId", "WorkOrderSnapshotId", "TenantId", "IntegrationId", "WorkOrderProviderId", "Status", "CreatedAt", "UpdatedAt", {DetailColumnList()})
            VALUES (@id, @woid, @snid, @tid, @iid, @pid, @status, @now, @now, {DetailParamList()})
            """;
        cmd.Parameters.AddWithValue("@id", Guid.CreateVersion7());
        cmd.Parameters.AddWithValue("@woid", workOrderId);
        cmd.Parameters.AddWithValue("@snid", interactionId);
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@iid", integrationId);
        cmd.Parameters.AddWithValue("@pid", order.WorkOrderProviderId);
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
        DateTimeOffset interactionCreatedAt,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO consumer_states ("ConsumerId", "ResumeToken", "LastProcessedInteractionCreatedAt", "UpdatedAt")
            VALUES (@id, @token::jsonb, @interactionCreatedAt, @now)
            ON CONFLICT ("ConsumerId") DO UPDATE
            SET "ResumeToken" = EXCLUDED."ResumeToken",
                "LastProcessedInteractionCreatedAt" = EXCLUDED."LastProcessedInteractionCreatedAt",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """;
        cmd.Parameters.AddWithValue("@id", consumerId);
        cmd.Parameters.AddWithValue("@token", resumeToken);
        cmd.Parameters.AddWithValue("@interactionCreatedAt", interactionCreatedAt);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
