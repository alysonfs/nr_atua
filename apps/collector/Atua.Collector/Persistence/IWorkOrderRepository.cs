using Atua.Collector.IService;

namespace Atua.Collector.Persistence;

/// <summary>
/// Contrato de persistência de ordens de serviço coletadas no iService.
/// </summary>
public interface IWorkOrderRepository
{
    /// <summary>
    /// Executa upsert do snapshot atual de cada OS para o tenant.
    /// Idempotente por (tenantId, providerOrderId).
    /// </summary>
    Task UpsertSnapshotsAsync(
        Guid tenantId,
        Guid commandId,
        CollectionResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Insere uma observação imutável por OS por coleta.
    /// Índice único em (tenantId, providerOrderId, commandId).
    /// </summary>
    Task InsertObservationsAsync(
        Guid tenantId,
        Guid commandId,
        CollectionResult result,
        CancellationToken cancellationToken = default);
}
