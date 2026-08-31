namespace Atua.Collector.IService;

/// <summary>
/// Serviço de coleta de ordens de serviço via Playwright no portal iService (ics-amer.midea.com).
/// </summary>
public interface IIServiceCollector
{
    /// <summary>
    /// Realiza login no portal iService e coleta as ordens de serviço por status.
    /// </summary>
    /// <param name="username">Usuário CAS (pode ser logado).</param>
    /// <param name="password">Senha CAS — NUNCA logar.</param>
    /// <param name="baseUrl">URL base do iService, se sobrescrita pela credencial; caso contrário usa o padrão.</param>
    /// <param name="historyWindowMonths">Janela histórica em meses (referência; a SPA filtra pelo template capturado).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado da coleta com contagens e listas de OS por status.</returns>
    /// <exception cref="CredentialRejectedEx">Quando o login CAS falhar por credencial inválida.</exception>
    /// <exception cref="IServiceUnavailableEx">Quando o portal estiver inacessível ou indisponível.</exception>
    Task<CollectionResult> CollectAsync(
        string username,
        string password,
        string? baseUrl,
        int historyWindowMonths,
        CancellationToken cancellationToken = default);
}
