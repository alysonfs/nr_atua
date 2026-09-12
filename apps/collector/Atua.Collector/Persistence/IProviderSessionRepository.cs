namespace Atua.Collector.Persistence;

/// <summary>
/// Repositório de sessões persistidas do provedor (RF-023/ADR-028) — coleção
/// <c>provider_sessions</c>. No máximo um documento por
/// <c>(tenant_id, provider_type)</c> (RF-023.5/RN-023.5); upsert a cada login bem-sucedido
/// ou invalidação, nunca append-only.
/// </summary>
public interface IProviderSessionRepository
{
    /// <summary>Cria os índices necessários se ainda não existirem. Idempotente.</summary>
    Task EnsureIndexesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca o <c>storage_state</c> de uma sessão válida e não expirada para
    /// <c>(tenantId, providerType)</c> (RF-023.1/RN-023.1). Retorna <c>null</c> se não
    /// existir sessão, se <c>valid = false</c>, ou se já expirada.
    /// </summary>
    Task<string?> GetValidStorageStateAsync(Guid tenantId, string providerType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Faz upsert da sessão para <c>(tenantId, providerType)</c> com <c>valid = true</c> e
    /// <c>expires_at</c> recalculado a partir de <paramref name="ttl"/> (RF-023.3/RN-023.3).
    /// </summary>
    Task SaveValidSessionAsync(
        Guid tenantId,
        string providerType,
        string storageState,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca a sessão de <c>(tenantId, providerType)</c> como <c>valid = false</c>
    /// (RF-023.4/RN-023.4). Não remove o documento nem apaga <c>storage_state</c> — apenas
    /// sinaliza que não deve mais ser reutilizada até o próximo login bem-sucedido.
    /// No-op (sem erro) se ainda não existir documento para a chave.
    /// </summary>
    Task InvalidateSessionAsync(Guid tenantId, string providerType, CancellationToken cancellationToken = default);
}
