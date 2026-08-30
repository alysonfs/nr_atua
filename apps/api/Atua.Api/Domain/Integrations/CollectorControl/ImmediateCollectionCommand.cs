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

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public ECollectorDeactivationReason? CancellationReason { get; private set; }

    public int AttemptCount { get; private set; }

    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

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
