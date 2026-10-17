namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Comando de envio do template inicial ao canal (ADR-042; arquitetura,
/// seção 5.3). Substitui a chamada síncrona a <c>ITriagemChannel</c> do
/// incremento anterior: a Master API persiste este comando na mesma
/// transação em que registra o aceite idempotente da triagem
/// (<see cref="Triagem"/>, ADR-039/ADR-040), e o processo separado do canal
/// o reivindica por polling (<c>claim</c>) e reporta o resultado
/// (<c>complete</c>) — nunca por chamada direta em processo.
///
/// Isolamento multi-tenant: <see cref="TenantId"/> e <see cref="IntegrationId"/>
/// são redundantes (mesmo padrão de <see cref="Triagem"/>/<see cref="TriagemTriggerOutbox"/>)
/// e usados para restringir o claim à credencial de serviço autenticada —
/// nunca ao corpo da requisição.
/// </summary>
public sealed class TriagemChannelCommand
{
    /// <summary>
    /// Limite de falhas transitórias consecutivas antes de concluir o
    /// comando como falha definitiva e mover a triagem para
    /// <c>FalhaDeContato</c> (ADR-042, "Decisão").
    /// </summary>
    public const int MaxTransientFailures = 5;

    private TriagemChannelCommand() { }

    public TriagemChannelCommand(Guid id, Guid tenantId, Guid integrationId, Guid triagemId, Guid workOrderId,
        string templateId, int templateVersion, string renderedContent, string idempotencyKey,
        Guid correlationId, DateTimeOffset now, string? recipientPhone = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        }

        if (integrationId == Guid.Empty)
        {
            throw new ArgumentException("IntegrationId não pode ser vazio.", nameof(integrationId));
        }

        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new ArgumentException("TemplateId não pode ser vazio.", nameof(templateId));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey não pode ser vazia.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(renderedContent))
        {
            throw new ArgumentException("RenderedContent não pode ser vazio.", nameof(renderedContent));
        }

        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        TriagemId = triagemId;
        WorkOrderId = workOrderId;
        TemplateId = templateId;
        TemplateVersion = templateVersion;
        RenderedContent = renderedContent;
        RecipientPhone = recipientPhone;
        IdempotencyKey = idempotencyKey;
        CorrelationId = correlationId;
        Status = ETriagemChannelCommandStatus.Pending;
        AttemptCount = 0;
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Redundante para isolamento multi-tenant, mesmo padrão de <see cref="Triagem"/>.</summary>
    public Guid TenantId { get; private set; }

    public Guid IntegrationId { get; private set; }

    public Guid TriagemId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    /// <summary>Identificador imutável da versão selecionada do template.</summary>
    public string TemplateId { get; private set; } = string.Empty;

    public int TemplateVersion { get; private set; }

    /// <summary>
    /// Snapshot renderizado no aceite da triagem. Retentativas sempre usam
    /// exatamente este conteúdo, mesmo após a publicação de outra versão.
    /// </summary>
    public string RenderedContent { get; private set; } = string.Empty;

    /// <summary>
    /// Snapshot do telefone de destino da OS no aceite da triagem. É dado
    /// pessoal e não deve ser escrito em logs; retentativas mantêm o mesmo
    /// destinatário.
    /// </summary>
    public string? RecipientPhone { get; private set; }

    /// <summary>
    /// Chave estável de idempotência (ADR-039, item 3) — a mesma em toda
    /// retentativa; nunca regenerada.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Correlação entre comando, resposta do canal e evento de entrega (arquitetura, seção 5.1).</summary>
    public Guid CorrelationId { get; private set; }

    public ETriagemChannelCommandStatus Status { get; private set; }

    /// <summary>Quantidade de falhas transitórias já registradas (<see cref="RecordTransientFailure"/>).</summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Também usado como token de concorrência otimista (mesmo padrão de
    /// <see cref="TriagemTriggerOutbox.ClaimedAtUtc"/>): muda a cada claim,
    /// protegendo contra uma instância do canal com lease expirada que tente
    /// gravar depois de o comando já ter sido reivindicado de novo.
    /// </summary>
    public DateTimeOffset? ClaimedAtUtc { get; private set; }

    public DateTimeOffset? ClaimExpiresAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Código seguro do último erro reportado pelo canal — nunca texto livre/mensagem de exceção.</summary>
    public string? LastErrorCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Transição aplicada pelo claim atômico (SQL bruto
    /// <c>TriagemChannelCommandPersistenceSchema.ClaimNextSql</c>): marca
    /// <see cref="ETriagemChannelCommandStatus.Claimed"/> com uma nova
    /// lease. Em produção esta transição é feita pelo UPDATE em SQL bruto —
    /// o EF apenas materializa a linha já atualizada (nunca chama este
    /// método); existe como método de domínio para permitir que fakes de
    /// teste simulem o mesmo efeito sem PostgreSQL real.
    /// </summary>
    internal void Claim(DateTimeOffset claimedAtUtc, DateTimeOffset claimExpiresAtUtc)
    {
        Status = ETriagemChannelCommandStatus.Claimed;
        ClaimedAtUtc = claimedAtUtc;
        ClaimExpiresAtUtc = claimExpiresAtUtc;
    }

    /// <summary>Conclui o comando com sucesso (ADR-042: triagem → <c>ContatoIniciado</c>).</summary>
    public void MarkCompletedSuccess(DateTimeOffset now)
    {
        ExigirClaimed(nameof(MarkCompletedSuccess));
        Status = ETriagemChannelCommandStatus.Completed;
        CompletedAtUtc = now;
    }

    /// <summary>Conclui o comando com falha definitiva (ADR-042: triagem → <c>FalhaDeContato</c>).</summary>
    public void MarkCompletedPermanentFailure(string? errorCode, DateTimeOffset now)
    {
        ExigirClaimed(nameof(MarkCompletedPermanentFailure));
        Status = ETriagemChannelCommandStatus.Failed;
        LastErrorCode = errorCode;
        CompletedAtUtc = now;
    }

    /// <summary>
    /// Registra uma falha transitória (ADR-042): incrementa
    /// <see cref="AttemptCount"/> e volta a <see cref="ETriagemChannelCommandStatus.Pending"/>
    /// (preservando <see cref="IdempotencyKey"/>) para nova tentativa. Ao
    /// atingir <see cref="MaxTransientFailures"/>, conclui como falha
    /// definitiva (<see cref="ETriagemChannelCommandStatus.Failed"/>) — o
    /// chamador deve então mover a triagem para <c>FalhaDeContato</c>.
    /// </summary>
    public void RecordTransientFailure(string? errorCode, DateTimeOffset now)
    {
        ExigirClaimed(nameof(RecordTransientFailure));

        AttemptCount++;
        LastErrorCode = errorCode;
        ClaimedAtUtc = null;
        ClaimExpiresAtUtc = null;

        if (AttemptCount >= MaxTransientFailures)
        {
            Status = ETriagemChannelCommandStatus.Failed;
            CompletedAtUtc = now;
        }
        else
        {
            Status = ETriagemChannelCommandStatus.Pending;
        }
    }

    private void ExigirClaimed(string operacao)
    {
        if (Status != ETriagemChannelCommandStatus.Claimed)
        {
            throw new InvalidOperationException(
                $"{operacao} exige Status=Claimed, mas o status atual é {Status}.");
        }
    }
}
