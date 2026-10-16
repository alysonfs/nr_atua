namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Nomes físicos e SQL de claim de <c>triagem_channel_commands</c> (ADR-042).
/// Mesmo padrão de <see cref="TriagemOutboxPersistenceSchema"/>: mantidos em
/// um único lugar para que o mapeamento EF Core e a implementação concreta
/// do repositório não divirjam silenciosamente.
/// </summary>
internal static class TriagemChannelCommandPersistenceSchema
{
    public const string TableName = "triagem_channel_commands";

    /// <summary>
    /// Restrição única da chave de idempotência (ADR-039/ADR-042) — no
    /// máximo um comando de canal por triagem/gatilho, para sempre.
    /// </summary>
    public const string IdempotencyUniqueIndexName = "uq_triagem_channel_commands_idempotency_key";

    /// <summary>Índice de apoio ao claim por tenant/integração + status.</summary>
    public const string ClaimDueIndexName = "ix_triagem_channel_commands_claim_due";

    /// <summary>Índice parcial (<c>Status = 'Claimed'</c>) de apoio ao claim por lease expirada.</summary>
    public const string ClaimLeaseIndexName = "ix_triagem_channel_commands_claim_lease";

    /// <summary>
    /// Reivindica, atomicamente, o próximo comando elegível
    /// (<c>Pending</c>, ou <c>Claimed</c> com lease expirada) da
    /// tenant/integração informada — tenant e integração vêm exclusivamente
    /// dos claims da credencial de serviço (ADR-042: "o corpo das
    /// requisições não poderá ampliar esse escopo"). Usa
    /// <c>FOR UPDATE SKIP LOCKED</c> para que múltiplas instâncias do canal
    /// reivindiquem concorrentemente sem entrega duplicada, sem lock em
    /// memória — a arbitragem é inteiramente do PostgreSQL. Lease expirada
    /// torna o comando elegível novamente sem alterar
    /// <c>IdempotencyKey</c> (ADR-042).
    /// </summary>
    public const string ClaimNextSql =
        $"""
         WITH claimed AS (
             SELECT "Id"
             FROM {TableName}
             WHERE "TenantId" = @tenantId
               AND "IntegrationId" = @integrationId
               AND (("Status" = 'Pending')
                 OR ("Status" = 'Claimed' AND "ClaimExpiresAtUtc" < @now))
             ORDER BY "CreatedAtUtc" ASC
             FOR UPDATE SKIP LOCKED
             LIMIT 1
         )
         UPDATE {TableName} AS c
         SET "Status" = 'Claimed',
             "ClaimedAtUtc" = @now,
             "ClaimExpiresAtUtc" = @claimExpiresAtUtc
         FROM claimed
         WHERE c."Id" = claimed."Id"
         RETURNING c.*
         """;
}
