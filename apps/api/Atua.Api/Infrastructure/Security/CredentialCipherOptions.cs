namespace Atua.Api.Infrastructure.Security;

/// <summary>
/// Configuração do cifrador de credenciais de integrações (ADR-004/ADR-018).
///
/// Em desenvolvimento/testes, configure <see cref="MasterKeyBase64"/> via
/// <c>dotnet user-secrets</c> (jamais versione a chave):
/// <code>
///   openssl rand -base64 32
///   dotnet user-secrets set "Integrations:CredentialCipher:MasterKeyBase64" "&lt;saída acima&gt;"
/// </code>
///
/// Em produção, configure <see cref="KmsKeyArn"/> via variável de ambiente ou
/// SSM Parameter (ex.: <c>Integrations__CredentialCipher__KmsKeyArn=arn:aws:kms:...</c>).
/// Quando o ARN estiver presente, a implementação KMS (v2) é usada para novos
/// registros; registros v1 continuam decifráveis pela chave local.
/// </summary>
public sealed class CredentialCipherOptions
{
    public const string SectionName = "Integrations:CredentialCipher";

    /// <summary>
    /// Chave mestra local (AES-256, 32 bytes, Base64). Usada apenas para
    /// AlgorithmVersion=1 (desenvolvimento/testes). Nunca versionar — use
    /// <c>dotnet user-secrets</c>.
    /// </summary>
    public string MasterKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// ARN ou alias da CMK do AWS KMS. Quando configurado, novos registros
    /// usam AlgorithmVersion=2 (wrap via KMS). Lido de IConfiguration, que
    /// aceita variáveis de ambiente (ex.: Integrations__CredentialCipher__KmsKeyArn).
    /// </summary>
    public string KmsKeyArn { get; set; } = string.Empty;
}
