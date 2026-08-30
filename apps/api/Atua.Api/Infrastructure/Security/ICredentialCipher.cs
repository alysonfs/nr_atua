namespace Atua.Api.Infrastructure.Security;

/// <summary>
/// Cifra/decifra segredos de configuração de integrações (ADR-004/ADR-018):
/// AES-256-GCM com chave de dados aleatória por integração, protegida por uma
/// "chave mestra" externa (KMS). Componente compartilhado — não deve ser
/// duplicado por outras features que precisem de cifra de segredos.
/// </summary>
public interface ICredentialCipher
{
    /// <summary>
    /// Gera uma nova chave de dados (DEK) para uma integração, cifrando-a com
    /// a chave mestra (envelope encryption). A operação pode envolver chamada
    /// de rede ao KMS e é portanto assíncrona.
    /// </summary>
    Task<EncryptedDataKey> CreateDataKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cifra um valor em claro utilizando a chave de dados informada.
    /// Nonce e tag são derivados desta operação e devem ser persistidos junto
    /// ao ciphertext.
    /// </summary>
    CipherResult Encrypt(byte[] dataKeyPlaintext, string plaintext);

    /// <summary>
    /// Decifra um valor cifrado com a chave de dados informada.
    /// </summary>
    string Decrypt(byte[] dataKeyPlaintext, string ciphertextBase64, byte[] nonce, byte[] tag);

    /// <summary>
    /// Decifra a chave de dados (DEK) protegida pela chave mestra (KMS).
    /// A operação pode envolver chamada de rede ao KMS e é portanto assíncrona.
    /// </summary>
    Task<byte[]> UnwrapDataKeyAsync(string dataKeyCiphertextBase64, string kmsKeyId,
        int algorithmVersion, CancellationToken cancellationToken = default);
}

/// <param name="Plaintext">DEK em claro — zerar da memória após uso.</param>
/// <param name="CiphertextBase64">DEK cifrada (envelope) para persistência.</param>
/// <param name="KmsKeyId">
/// Identificador da chave mestra usada. Para v1 (local) é um valor de
/// configuração opaco; para v2 (KMS) é o ARN da CMK.
/// </param>
/// <param name="AlgorithmVersion">1 = wrap local AES-GCM; 2 = wrap via AWS KMS.</param>
public sealed record EncryptedDataKey(byte[] Plaintext, string CiphertextBase64,
    string KmsKeyId, int AlgorithmVersion);

public sealed record CipherResult(string CiphertextBase64, byte[] Nonce, byte[] Tag);
