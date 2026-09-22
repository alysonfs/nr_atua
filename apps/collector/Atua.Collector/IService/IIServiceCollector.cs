namespace Atua.Collector.IService;

/// <summary>
/// Serviço de coleta de ordens de serviço via Playwright no portal iService (ics-amer.midea.com).
/// </summary>
public interface IIServiceCollector
{
    /// <summary>
    /// Realiza login no portal iService e coleta as ordens de serviço por status.
    /// </summary>
    /// <param name="tenantId">
    /// Tenant dono deste ciclo de coleta — usado para registrar cada interação real com o
    /// provedor em <c>provider_interactions</c> (RF-022/ADR-028), além do commandId.
    /// </param>
    /// <param name="commandId">
    /// CommandId do ciclo corrente (mesmo já reivindicado via ClaimAsync) — usado para
    /// correlacionar entradas do log de diagnóstico local
    /// (<see cref="Atua.Collector.Diagnostics.IIServiceDebugLogger"/>) e os documentos de
    /// <c>provider_interactions</c> com o ciclo, sem depender de estado ambiente implícito.
    /// </param>
    /// <param name="username">Usuário CAS (pode ser logado).</param>
    /// <param name="password">Senha CAS — NUNCA logar.</param>
    /// <param name="baseUrl">URL base do iService, se sobrescrita pela credencial; caso contrário usa o padrão.</param>
    /// <param name="historyWindowMonths">Janela histórica em meses (referência; a SPA filtra pelo template capturado).</param>
    /// <param name="pendingDetailWorkOrderIds">
    /// IDs de OS (<c>WorkOrderProviderId</c>) desta integração pendentes de busca de detalhe,
    /// devolvidos pelo <c>claim</c> (RF-026/ADR-031) — decidido pelo Consumer, não pelo Collector.
    /// </param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado da coleta com contagens e listas de OS por status.</returns>
    /// <exception cref="CredentialRejectedEx">Quando o login CAS falhar por credencial inválida.</exception>
    /// <exception cref="IServiceUnavailableEx">Quando o portal estiver inacessível ou indisponível.</exception>
    Task<CollectionResult> CollectAsync(
        Guid tenantId,
        Guid commandId,
        string username,
        string password,
        string? baseUrl,
        int historyWindowMonths,
        IReadOnlyList<string> pendingDetailWorkOrderIds,
        CancellationToken cancellationToken = default);
}
