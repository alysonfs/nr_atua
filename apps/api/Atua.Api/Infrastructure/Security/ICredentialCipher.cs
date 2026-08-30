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
    /// a chave mestra (envelope encryption).
    /// </summary>
    EncryptedDataKey CreateDataKey();

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
    /// </summary>
    byte[] UnwrapDataKey(string dataKeyCiphertextBase64, Guid kmsKeyId);
}

public sealed record EncryptedDataKey(byte[] Plaintext, string CiphertextBase64, Guid KmsKeyId);

public sealed record CipherResult(string CiphertextBase64, byte[] Nonce, byte[] Tag);
