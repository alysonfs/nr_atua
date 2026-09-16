namespace Atua.Api.Domain.Integrations.CollectorControl;

/// <summary>
/// Comando durável de coleta imediata (RF-008.4/ADR-020).
///
/// Escopo RF-008: o comando é somente uma solicitação rastreável. Nada o
/// executa neste portão. O payload nunca contém credencial, cookie, URL privada
/// ou qualquer segredo (RN-008.6).
/// </summary>
public sealed class ImmediateCollectionCommand
{
    private ImmediateCollectionCommand() { }

    public ImmediateCollectionCommand(Guid id, Guid tenantId, Guid integrationId, Guid providerId,
        DateTimeOffset requestedAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        ProviderId = providerId;
        RequestedAtUtc = requestedAtUtc;
        Status = EImmediateCollectionCommandStatus.Pending;
    }

    public Guid Id { get; private set; }

    /// <summary>Redundante para consistência/auditoria e isolamento (RN-008.1).</summary>
    public Guid TenantId { get; private set; }

    public Guid IntegrationId { get; private set; }

    /// <summary>Chave do provedor da integração (ADR-020: <c>ProviderKey</c>).</summary>
    public Guid ProviderId { get; private set; }

    public EImmediateCollectionCommandStatus Status { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset? ClaimedAtUtc { get; private set; }

    /// <summary>
    /// Prazo máximo para conclusão após o claim (ADR-021/D2: 30 minutos).
    /// Null enquanto o comando não estiver em <c>Claimed</c>.
    /// </summary>
    public DateTimeOffset? ClaimExpiresAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public ECollectorDeactivationReason? CancellationReason { get; private set; }

    /// <summary>
    /// Motivo da falha quando <c>Status == Failed</c> (ADR-021/D4).
    /// </summary>
    public ECommandFailureReason? FailureReason { get; private set; }

    public int AttemptCount { get; private set; }

    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// Transita o comando de <c>Pending</c> para <c>Claimed</c>
    /// (ADR-021/D2 e D9). Incrementa <c>AttemptCount</c> e define o
    /// prazo de expiração do claim.
    /// </summary>
    public bool TryClaim(DateTimeOffset now, TimeSpan claimTimeout)
    {
        if (Status != EImmediateCollectionCommandStatus.Pending) return false;

        Status = EImmediateCollectionCommandStatus.Claimed;
        ClaimedAtUtc = now;
        ClaimExpiresAtUtc = now.Add(claimTimeout);
        AttemptCount++;
        ConcurrencyToken = Guid.NewGuid();
        return true;
    }

    /// <summary>
    /// Conclui o comando com <c>Succeeded</c> ou <c>Cancelled</c>
    /// (ADR-021/D4). Idempotente: retorna <c>true</c> se o estado terminal
    /// já era o esperado.
    /// </summary>
    public bool TryComplete(EImmediateCollectionCommandStatus outcome, DateTimeOffset now)
    {
        if (outcome is not (EImmediateCollectionCommandStatus.Succeeded
            or EImmediateCollectionCommandStatus.Cancelled))
        {
            throw new ArgumentException("Use TryFail para falhas.", nameof(outcome));
        }

        if (Status == outcome) return true;          // idempotência
        if (Status != EImmediateCollectionCommandStatus.Claimed) return false;

        Status = outcome;
        CompletedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
        return true;
    }

    /// <summary>
    /// Conclui o comando como <c>Failed</c> com o motivo de falha
    /// (ADR-021/D4). Idempotente: retorna <c>true</c> se já estava em
    /// <c>Failed</c>.
    /// </summary>
    public bool TryFail(ECommandFailureReason reason, DateTimeOffset now)
    {
        if (Status == EImmediateCollectionCommandStatus.Failed) return true; // idempotência
        if (Status != EImmediateCollectionCommandStatus.Claimed) return false;

        Status = EImmediateCollectionCommandStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
        return true;
    }

    /// <summary>
    /// Expira um comando <c>Claimed</c> órfão para <c>Failed/ClaimTimeout</c>
    /// (ADR-021/D2). Chamado pelo job de reconciliação e pela verificação
    /// preguiçosa no claim.
    /// </summary>
    public bool TryExpireTimeout(DateTimeOffset now)
    {
        if (Status != EImmediateCollectionCommandStatus.Claimed) return false;
        if (ClaimExpiresAtUtc is null || ClaimExpiresAtUtc > now) return false;

        Status = EImmediateCollectionCommandStatus.Failed;
        FailureReason = ECommandFailureReason.ClaimTimeout;
        CompletedAtUtc = now;
        CancellationReason = ECollectorDeactivationReason.ClaimTimeout;
        ConcurrencyToken = Guid.NewGuid();
        return true;
    }

    /// <summary>
    /// Cancela o comando. <c>Cancelled</c> é terminal: um comando cancelado
    /// nunca retorna a <c>Pending</c> (RF-008.7/RN-008.4).
    /// </summary>
    public void Cancel(ECollectorDeactivationReason reason, DateTimeOffset now)
    {
        if (Status != EImmediateCollectionCommandStatus.Pending)
        {
            // Somente pendências são canceladas (RN-008.5); um comando já
            // reivindicado ou concluído não é desfeito à força (ADR-020).
            return;
        }

        Status = EImmediateCollectionCommandStatus.Cancelled;
        CancellationReason = reason;
        CompletedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }
}
