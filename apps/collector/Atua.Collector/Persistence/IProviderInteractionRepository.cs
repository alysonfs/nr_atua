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

    /// <summary>
    /// Remove um documento já processado com sucesso pelo consumer (ADR-030, decisão 1).
    /// Chamado apenas depois que a transação Postgres correspondente já deu commit —
    /// nunca antes. Best-effort: falha ao apagar é logada como aviso e NUNCA propagada,
    /// mesma postura de <see cref="InsertInteractionAsync"/>. Idempotente — apagar um
    /// documento já removido apenas retorna <c>DeletedCount = 0</c>, sem erro.
    /// </summary>
    Task DeleteProcessedAsync(Guid interactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove todos os documentos com <c>created_at</c> menor ou igual a
    /// <paramref name="watermark"/> (ADR-030, decisão 2 — marca d'água de
    /// <c>ProviderInteractionCleanupJob</c>). Diferente de <see cref="DeleteProcessedAsync"/>,
    /// propaga exceção — quem chama (o job de limpeza) já trata falha por execução sem
    /// derrubar o processo. Retorna a quantidade de documentos removidos.
    /// </summary>
    Task<long> DeleteProcessedUpToAsync(DateTimeOffset watermark, CancellationToken cancellationToken = default);
}
