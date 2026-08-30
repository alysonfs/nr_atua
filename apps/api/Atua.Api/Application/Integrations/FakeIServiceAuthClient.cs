namespace Atua.Api.Application.Integrations;

/// <summary>
/// Implementação de teste/mock de <see cref="IIServiceAuthClient"/>
/// (ADR-018). NÃO representa integração real com o iService — trata-se de um
/// substituto explícito enquanto a especificação técnica do protocolo real de
/// autenticação do provedor não está disponível (ver ADR-018, seção RF-007).
///
/// Regra determinística de teste (documentada, sem qualquer pretensão de
/// representar o comportamento real do provedor):
/// - falha (Failed) quando a senha for vazia/em branco ou for exatamente a
///   string "invalid" (case-insensitive) — simula credencial recusada;
/// - sucesso (Succeeded) em qualquer outro caso.
///
/// TODO(ADR-018): substituir por uma implementação real assim que houver
/// documentação oficial do provedor, acesso a ambiente de sandbox, ou
/// especificação técnica repassada pelo cliente do projeto.
/// </summary>
public sealed class FakeIServiceAuthClient : IIServiceAuthClient
{
    public Task<IServiceAuthResult> TryAuthenticateAsync(
        IServiceCredentialPayload credential, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential.Password) ||
            string.Equals(credential.Password, "invalid", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new IServiceAuthResult(false, "fake_client_simulated_failure"));
        }

        return Task.FromResult(new IServiceAuthResult(true, null));
    }
}
