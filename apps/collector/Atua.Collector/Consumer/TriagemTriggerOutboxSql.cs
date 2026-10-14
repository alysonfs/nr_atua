namespace Atua.Collector.Consumer;

/// <summary>
/// SQL de escrita em <c>triagem_trigger_outbox</c> (ADR-041) usada pelo
/// Collector. O nome da tabela e as colunas são um contrato implícito com a
/// Master API (<c>Atua.Api.Infrastructure.Persistence.TriagemOutboxPersistenceSchema</c>
/// e <c>Atua.Api.Domain.Triagens.TriagemTriggerOutbox</c>), que lê/drena esta
/// mesma tabela em outro processo — ver ADR-041, "Consequências negativas",
/// e o mesmo critério de reavaliação do ADR-032 para
/// <c>WorkOrderTerminalStatuses</c>.
///
/// <c>ON CONFLICT DO NOTHING</c> sobre a restrição única
/// (TenantId, IntegrationId, WorkOrderProviderId) — mesma chave de
/// idempotência do agregado Triagem (ADR-039/ADR-040) — garante que, mesmo
/// que a mesma OS apareça como <c>assigned</c> em várias interações (antes
/// ou depois do deploy deste incremento), no máximo uma linha de outbox seja
/// criada para ela, para sempre.
/// </summary>
internal static class TriagemTriggerOutboxSql
{
    public const string InsertPendingCommandText =
        """
        INSERT INTO triagem_trigger_outbox
            ("Id", "TenantId", "IntegrationId", "WorkOrderId", "WorkOrderProviderId",
             "OccurredAtUtc", "Status", "AttemptCount", "NextAttemptAtUtc", "CreatedAtUtc")
        VALUES
            (@id, @tid, @iid, @woid, @pid,
             @occurredAt, 'Pending', 0, @occurredAt, @now)
        ON CONFLICT ("TenantId", "IntegrationId", "WorkOrderProviderId") DO NOTHING
        """;
}
