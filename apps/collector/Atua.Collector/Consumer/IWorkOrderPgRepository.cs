namespace Atua.Collector.Consumer;

/// <summary>
/// Contrato do repositório PostgreSQL do consumer de <c>provider_interactions</c>. Extraído
/// de <see cref="WorkOrderPgRepository"/> para permitir substituição em testes unitários do
/// <see cref="ProviderInteractionConsumerWorker"/> e do <see cref="ProviderInteractionCleanupJob"/>
/// (ADR-030) sem depender de uma conexão Postgres real.
/// </summary>
public interface IWorkOrderPgRepository
{
    /// <inheritdoc cref="WorkOrderPgRepository.ProcessInteractionAsync"/>
    Task ProcessInteractionAsync(
        Guid interactionId,
        Guid tenantId,
        IReadOnlyList<ProviderWorkOrderData> orders,
        string resumeToken,
        string consumerId,
        DateTimeOffset interactionCreatedAt,
        string interactionType,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="WorkOrderPgRepository.AdvanceResumeTokenAsync"/>
    Task AdvanceResumeTokenAsync(
        string resumeToken,
        string consumerId,
        DateTimeOffset interactionCreatedAt,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="WorkOrderPgRepository.GetResumeTokenAsync"/>
    Task<string?> GetResumeTokenAsync(string consumerId, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="WorkOrderPgRepository.ClearResumeTokenAsync"/>
    Task ClearResumeTokenAsync(string consumerId, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="WorkOrderPgRepository.GetLastProcessedInteractionCreatedAtAsync"/>
    Task<DateTimeOffset?> GetLastProcessedInteractionCreatedAtAsync(
        string consumerId,
        CancellationToken cancellationToken = default);
}
