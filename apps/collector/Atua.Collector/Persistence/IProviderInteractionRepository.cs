namespace Atua.Collector.Persistence;

/// <summary>
/// Contrato de persistência de interações brutas com o provedor (RF-022, ADR-028).
/// </summary>
public interface IProviderInteractionRepository
{
    /// <summary>
    /// Cria os índices necessários se ainda não existirem. Idempotente — seguro chamar a
    /// cada startup.
    /// </summary>
    Task EnsureIndexesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Insere um documento por interação real com o provedor — append-only, sem upsert
    /// (RF-022.1/RN-022.1). Chamado tanto para sucesso quanto para falha (RF-022.4/RN-022.4).
    /// </summary>
    Task InsertInteractionAsync(
        Guid tenantId,
        Guid commandId,
        string providerType,
        string interactionType,
        IReadOnlyDictionary<string, object?> request,
        IReadOnlyList<object?>? orders,
        bool success,
        string? errorMessage,
        CancellationToken cancellationToken = default);
}
