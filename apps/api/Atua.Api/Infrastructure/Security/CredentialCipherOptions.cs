namespace Atua.Api.Infrastructure.Security;

/// <summary>
/// Configuração do cifrador de credenciais de integrações (ADR-004/ADR-018).
///
/// TODO: <see cref="MasterKeyBase64"/> é um placeholder para desenvolvimento e
/// testes. Em produção, a chave mestra deve ser gerenciada via AWS KMS
/// (decisão de infraestrutura, fora do escopo deste componente).
/// </summary>
public sealed class CredentialCipherOptions
{
    public const string SectionName = "Integrations:CredentialCipher";

    public string MasterKeyBase64 { get; set; } = string.Empty;

    public Guid KmsKeyId { get; set; } = Guid.Empty;

    public int AlgorithmVersion { get; set; } = 1;
}
