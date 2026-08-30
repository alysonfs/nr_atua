namespace Atua.Api.Application.Integrations;

/// <summary>
/// Abstração da autenticação junto ao iService (RF-007/ADR-018).
///
/// LACUNA TÉCNICA EXPLÍCITA (ADR-018): não há, no momento, especificação
/// técnica do protocolo real de autenticação do iService (se é CAS clássico,
/// se expõe endpoint REST, formato de resposta, cookies envolvidos). Esta
/// interface isola essa incerteza do restante do sistema. Não implemente aqui
/// nenhuma lógica real de scraping/Playwright/automação de coleta — o Agente
/// Coletor está fora de escopo.
/// </summary>
public interface IIServiceAuthClient
{
    Task<IServiceAuthResult> TryAuthenticateAsync(
        IServiceCredentialPayload credential, CancellationToken cancellationToken);
}

public sealed record IServiceCredentialPayload(string Username, string Password, string? BaseUrl);

public sealed record IServiceAuthResult(bool Succeeded, string? FailureReasonForLog);
