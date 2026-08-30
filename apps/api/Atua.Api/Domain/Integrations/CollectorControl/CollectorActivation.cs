namespace Atua.Api.Domain.Integrations.CollectorControl;

/// <summary>
/// Intenção operacional atual do Agente Coletor para uma integração
/// (RF-008/ADR-020). Uma linha por <see cref="IntegrationId"/>.
///
/// Não representa sessão, credencial ou acesso ao iService: apenas estado.
/// </summary>
public sealed class CollectorActivation
{
    private CollectorActivation() { }

    public CollectorActivation(Guid id, Guid tenantId, Guid integrationId)
    {
        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        Status = ECollectorActivationStatus.Inactive; // RF-008.1
    }

    public Guid Id { get; private set; }

    /// <summary>Redundante para consistência/isolamento multi-tenant (RN-008.1).</summary>
    public Guid TenantId { get; private set; }

    public Guid IntegrationId { get; private set; }

    public ECollectorActivationStatus Status { get; private set; }

    public DateTimeOffset? ActivatedAtUtc { get; private set; }

    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    public ECollectorDeactivationReason? DeactivationReason { get; private set; }

    /// <summary>Token de concorrência otimista (ADR-020).</summary>
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public void Activate(DateTimeOffset now)
    {
        Status = ECollectorActivationStatus.Active;
        ActivatedAtUtc = now;
        DeactivatedAtUtc = null;
        DeactivationReason = null;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Deactivate(ECollectorDeactivationReason reason, DateTimeOffset now)
    {
        Status = ECollectorActivationStatus.Inactive;
        DeactivatedAtUtc = now;
        DeactivationReason = reason;
        ConcurrencyToken = Guid.NewGuid();
    }
}
