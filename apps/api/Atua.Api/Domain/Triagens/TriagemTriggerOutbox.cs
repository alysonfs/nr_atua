namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Linha de outbox do gatilho de início de triagem (ADR-041).
///
/// Gravada pelo Collector (via SQL direto — ver
/// <c>Atua.Collector.Consumer.WorkOrderPgRepository</c> — não por este tipo),
/// na mesma transação Postgres do upsert de <c>work_order</c>, quando uma OS
/// atinge status <c>assigned</c>. Drenada por um <c>BackgroundService</c>
/// hospedado nesta Master API (<c>TriagemOutboxDrainWorker</c>), que
/// reivindica linhas em lote com <c>FOR UPDATE SKIP LOCKED</c> + lease
/// (nunca lock em memória) e chama <see cref="Atua.Api.Application.Triagens.StartTriagemUseCase"/>
/// diretamente, em processo.
///
/// <see cref="WorkOrderId"/> é mantido apenas para rastreabilidade — o
/// gatilho em si é identificado, para fins de idempotência/claim, por
/// tenant + integração + <see cref="WorkOrderProviderId"/> (mesma chave do
/// agregado <see cref="Triagem"/>, ADR-039/ADR-040): a Master API sempre
/// revalida o estado atual da OS antes de criar a triagem, nunca confia
/// cegamente no <see cref="WorkOrderId"/> gravado aqui.
/// </summary>
public sealed class TriagemTriggerOutbox
{
    private TriagemTriggerOutbox() { }

    public TriagemTriggerOutbox(Guid id, Guid tenantId, Guid integrationId, Guid workOrderId,
        string workOrderProviderId, DateTimeOffset occurredAtUtc, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(workOrderProviderId))
        {
            throw new ArgumentException("WorkOrderProviderId não pode ser vazio.", nameof(workOrderProviderId));
        }

        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        WorkOrderId = workOrderId;
        WorkOrderProviderId = workOrderProviderId;
        OccurredAtUtc = occurredAtUtc;
        Status = ETriagemTriggerOutboxStatus.Pending;
        AttemptCount = 0;
        NextAttemptAtUtc = occurredAtUtc;
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Redundante para isolamento multi-tenant, mesmo padrão de <see cref="Triagem"/>.</summary>
    public Guid TenantId { get; private set; }

    public Guid IntegrationId { get; private set; }

    /// <summary>Apenas rastreabilidade — ver resumo da classe.</summary>
    public Guid WorkOrderId { get; private set; }

    /// <summary>Identificador externo da OS no provedor — parte da chave de claim/idempotência.</summary>
    public string WorkOrderProviderId { get; private set; } = string.Empty;

    /// <summary>Instante (gerado pelo Collector) em que a OS foi observada como <c>assigned</c>.</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public ETriagemTriggerOutboxStatus Status { get; private set; }

    /// <summary>
    /// Quantidade de tentativas de processamento que falharam. Incrementado
    /// somente em falha (<see cref="RecordFailure"/>) — nunca no claim em si
    /// (requisito do ADR-041).
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>Instante a partir do qual a linha volta a ser elegível para claim.</summary>
    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    /// <summary>
    /// Também usado como token de concorrência otimista (ver mapeamento EF):
    /// muda a cada claim, protegendo contra um worker com lease expirada que
    /// tente gravar depois de a linha já ter sido reivindicada de novo.
    /// </summary>
    public DateTimeOffset? ClaimedAtUtc { get; private set; }

    public DateTimeOffset? ClaimExpiresAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    /// <summary>Código seguro do último erro — nunca a mensagem da exceção (ver <see cref="ETriagemOutboxErrorCode"/>).</summary>
    public ETriagemOutboxErrorCode? LastErrorCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Transição aplicada pelo claim em lote (<c>ClaimBatchSql</c>): marca
    /// <c>Processing</c> com uma nova lease. Em produção esta transição é
    /// feita pelo UPDATE em SQL bruto — o EF apenas materializa a linha já
    /// atualizada (nunca chama este método); existe como método de domínio
    /// (em vez de setters públicos) para permitir que fakes de teste (ex.:
    /// <c>FakeTriagemOutboxRepository</c>) simulem o mesmo efeito sem
    /// reflexão nem PostgreSQL real.
    /// </summary>
    internal void Claim(DateTimeOffset claimedAtUtc, DateTimeOffset claimExpiresAtUtc)
    {
        Status = ETriagemTriggerOutboxStatus.Processing;
        ClaimedAtUtc = claimedAtUtc;
        ClaimExpiresAtUtc = claimExpiresAtUtc;
    }

    /// <summary>Marca como processada com sucesso (ADR-039: Created ou Existing).</summary>
    public void MarkProcessed(DateTimeOffset now)
    {
        ExigirProcessing(nameof(MarkProcessed));
        Status = ETriagemTriggerOutboxStatus.Processed;
        ProcessedAtUtc = now;
    }

    /// <summary>
    /// Ignora definitivamente, sem retry: a Master API revalidou o estado
    /// atual da OS (<see cref="ETriagemOutboxErrorCode.WorkOrderNotFound"/> ou
    /// <see cref="ETriagemOutboxErrorCode.WorkOrderNotEligible"/>).
    /// </summary>
    public void MarkSkipped(ETriagemOutboxErrorCode errorCode, DateTimeOffset now)
    {
        ExigirProcessing(nameof(MarkSkipped));
        Status = ETriagemTriggerOutboxStatus.Skipped;
        LastErrorCode = errorCode;
        ProcessedAtUtc = now;
    }

    /// <summary>
    /// Registra uma falha de processamento (incrementa <see cref="AttemptCount"/>).
    /// Quando o novo <see cref="AttemptCount"/> atingir <paramref name="maxAttempts"/>,
    /// transita para <see cref="ETriagemTriggerOutboxStatus.Failed"/> (ignorando
    /// <paramref name="nextAttemptAtUtc"/>); caso contrário volta a
    /// <see cref="ETriagemTriggerOutboxStatus.Pending"/> com o próximo instante
    /// de tentativa calculado externamente (backoff exponencial, sem jitter).
    /// </summary>
    public void RecordFailure(ETriagemOutboxErrorCode errorCode, DateTimeOffset now, int maxAttempts,
        DateTimeOffset nextAttemptAtUtc)
    {
        ExigirProcessing(nameof(RecordFailure));

        AttemptCount++;
        LastErrorCode = errorCode;
        ClaimedAtUtc = null;
        ClaimExpiresAtUtc = null;

        if (AttemptCount >= maxAttempts)
        {
            Status = ETriagemTriggerOutboxStatus.Failed;
            ProcessedAtUtc = now;
        }
        else
        {
            Status = ETriagemTriggerOutboxStatus.Pending;
            NextAttemptAtUtc = nextAttemptAtUtc;
        }
    }

    private void ExigirProcessing(string operacao)
    {
        if (Status != ETriagemTriggerOutboxStatus.Processing)
        {
            throw new InvalidOperationException(
                $"{operacao} exige Status=Processing (claimed), mas o status atual é {Status}.");
        }
    }
}
