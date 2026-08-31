namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Estado atual de uma OS por tenant (RF-017).
/// Chave de identidade: (TenantId, ProviderId) — ver RN-017.6.
/// Status é string crua do provedor, sem enum (DP-017.2 resolvida).
/// </summary>
public sealed class WorkOrder
{
    private WorkOrder() { }

    public WorkOrder(Guid id, Guid tenantId, string providerId, string status, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        ProviderId = providerId;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Redundante para isolamento multi-tenant (RN-017.6).</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Identificador externo da OS no provedor (ex.: workOrderId do iService).</summary>
    public string ProviderId { get; private set; } = string.Empty;

    /// <summary>
    /// Status atual da OS — string crua retornada pelo provedor, sem mapeamento (DP-017.2).
    /// </summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Instante UTC em que a OS foi vista pela primeira vez.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Instante UTC da atualização mais recente.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Atualiza o status e <see cref="UpdatedAt"/>.
    /// </summary>
    public void UpdateStatus(string newStatus, DateTimeOffset now)
    {
        Status = newStatus;
        UpdatedAt = now;
    }

    /// <summary>
    /// Atualiza apenas <see cref="UpdatedAt"/> (mesmo status — RF-017.2).
    /// </summary>
    public void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
    }
}
