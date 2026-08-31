using Atua.Collector.IService;

namespace Atua.Collector.Persistence;

/// <summary>
/// Contrato de persistência de snapshots de ordens de serviço (RF-016).
/// </summary>
public interface IWorkOrderRepository
{
    /// <summary>
    /// Insere um snapshot por OS coletada — append-only (RF-016.1).
    /// Idempotente por re-execução via índice único (tenant_id, provider_id, command_id) (DP-016.1).
    /// OS sem provider_id válido são descartadas com log Warning (RF-016.5).
    /// </summary>
    Task InsertSnapshotsAsync(
        Guid tenantId,
        Guid commandId,
        string providerType,
        CollectionResult result,
        CancellationToken cancellationToken = default);
}
