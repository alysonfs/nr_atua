namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Nomes físicos e SQL de claim de <c>triagem_trigger_outbox</c> (ADR-041).
/// Mantidos num único lugar — mesmo padrão de <see cref="TriagemPersistenceSchema"/>
/// — para que o mapeamento EF Core (índices) e a implementação concreta do
/// repositório não divirjam silenciosamente.
///
/// O nome e as colunas desta tabela também são um contrato implícito com o
/// Collector (<c>Atua.Collector.Consumer.WorkOrderPgRepository</c>), que
/// escreve nela via SQL direto, fora deste processo (ADR-041, "Consequências
/// negativas": risco de divergência de schema entre os dois projetos — mesmo
/// critério de reavaliação do ADR-032 para <c>WorkOrderTerminalStatuses</c>).
/// </summary>
internal static class TriagemOutboxPersistenceSchema
{
    public const string TableName = "triagem_trigger_outbox";

    /// <summary>
    /// Restrição única (TenantId, IntegrationId, WorkOrderProviderId) — mesma
    /// chave de idempotência do agregado <c>Triagem</c> (ADR-039/ADR-040).
    /// Também o alvo do <c>ON CONFLICT DO NOTHING</c> usado pelo Collector.
    /// </summary>
    public const string IdempotencyUniqueIndexName = "uq_triagem_trigger_outbox_tenant_integration_workorder";

    /// <summary>Índice de apoio ao claim de linhas <c>Pending</c> vencidas.</summary>
    public const string ClaimDueIndexName = "ix_triagem_trigger_outbox_claim_due";

    /// <summary>Índice parcial (<c>Status = 'Processing'</c>) de apoio ao claim por lease expirada.</summary>
    public const string ClaimLeaseIndexName = "ix_triagem_trigger_outbox_claim_lease";

    /// <summary>
    /// Reivindica, em lote, linhas <c>Pending</c> vencidas (<c>NextAttemptAtUtc &lt;= @now</c>)
    /// ou <c>Processing</c> com lease expirada (<c>ClaimExpiresAtUtc &lt; @now</c>),
    /// marcando-as <c>Processing</c> com uma nova lease
    /// (<c>@now</c>/<c>@claimExpiresAtUtc</c>). Usa <c>FOR UPDATE SKIP LOCKED</c>
    /// para que múltiplas instâncias da Master API possam drenar
    /// concorrentemente sem reivindicar a mesma linha e sem lock em memória —
    /// a arbitragem é inteiramente do PostgreSQL.
    /// </summary>
    public const string ClaimBatchSql =
        $"""
         WITH claimed AS (
             SELECT "Id"
             FROM {TableName}
             WHERE ("Status" = 'Pending' AND "NextAttemptAtUtc" <= @now)
                OR ("Status" = 'Processing' AND "ClaimExpiresAtUtc" < @now)
             ORDER BY "NextAttemptAtUtc" ASC
             FOR UPDATE SKIP LOCKED
             LIMIT @batchSize
         )
         UPDATE {TableName} AS o
         SET "Status" = 'Processing',
             "ClaimedAtUtc" = @now,
             "ClaimExpiresAtUtc" = @claimExpiresAtUtc
         FROM claimed
         WHERE o."Id" = claimed."Id"
         RETURNING o.*
         """;
}
