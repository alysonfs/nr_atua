namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Entrada append-only de histórico de status de OS (RF-017).
/// Nova entrada somente quando o status muda (DP-017.1 resolvida — RF-017.2).
/// </summary>
public sealed class WorkOrderHistory
{
    private WorkOrderHistory() { }

    public WorkOrderHistory(
        Guid id,
        Guid workOrderId,
        Guid workOrderSnapshotId,
        Guid tenantId,
        string providerId,
        string status,
        DateTimeOffset createdAt)
    {
        Id = id;
        WorkOrderId = workOrderId;
        WorkOrderSnapshotId = workOrderSnapshotId;
        TenantId = tenantId;
        ProviderId = providerId;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>FK para <see cref="WorkOrder.Id"/>.</summary>
    public Guid WorkOrderId { get; private set; }

    /// <summary>
    /// Referência ao _id do documento MongoDB work_order_snapshots (RF-017.4).
    /// Não é FK de banco — rastreabilidade por convenção de aplicação
    /// (ADR-023, nota sobre FK cruzada).
    /// </summary>
    public Guid WorkOrderSnapshotId { get; private set; }

    /// <summary>Redundante para isolamento multi-tenant (RN-017.6).</summary>
    public Guid TenantId { get; private set; }

    public string ProviderId { get; private set; } = string.Empty;

    /// <summary>Status observado no momento desta entrada — string crua (DP-017.2).</summary>
    public string Status { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
