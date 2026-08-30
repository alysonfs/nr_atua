using System.Security.Cryptography;

namespace Atua.Api.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="ICredentialCipher"/> usando AES-256-GCM
/// (ADR-004/ADR-018), com "envelope encryption": cada integração recebe uma
/// chave de dados (DEK) aleatória, que por sua vez é cifrada por uma chave
/// mestra.
///
/// TODO(ADR-004/infra): a chave mestra usada aqui vem de configuração
/// (<see cref="CredentialCipherOptions.MasterKeyBase64"/>) como placeholder de
/// desenvolvimento/teste. A integração real com AWS KMS (wrap/unwrap da DEK
/// via KMS, rotação e IAM de menor privilégio) é uma decisão de infraestrutura
/// a ser conduzida pelo aws-architect; este componente isola essa
/// substituição futura sem exigir mudança no restante da aplicação.
/// </summary>
public sealed class AesGcmCredentialCipher(
    Microsoft.Extensions.Options.IOptions<CredentialCipherOptions> options) : ICredentialCipher
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int DataKeySizeBytes = 32; // AES-256
    private readonly CredentialCipherOptions cipherOptions = options.Value;

    public EncryptedDataKey CreateDataKey()
    {
        var dataKeyPlaintext = RandomNumberGenerator.GetBytes(DataKeySizeBytes);
        var wrapped = WrapDataKey(dataKeyPlaintext);
        return new EncryptedDataKey(dataKeyPlaintext, wrapped, cipherOptions.KmsKeyId);
    }

    public CipherResult Encrypt(byte[] dataKeyPlaintext, string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var tag = new byte[TagSizeBytes];
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];

        using var aesGcm = new AesGcm(dataKeyPlaintext, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return new CipherResult(Convert.ToBase64String(ciphertext), nonce, tag);
    }

    public string Decrypt(byte[] dataKeyPlaintext, string ciphertextBase64, byte[] nonce, byte[] tag)
    {
        var ciphertext = Convert.FromBase64String(ciphertextBase64);
        var plaintextBytes = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(dataKeyPlaintext, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        return System.Text.Encoding.UTF8.GetString(plaintextBytes);
    }

    public byte[] UnwrapDataKey(string dataKeyCiphertextBase64, Guid kmsKeyId)
    {
        var masterKey = GetMasterKey();
        var payload = Convert.FromBase64String(dataKeyCiphertextBase64);
        var nonce = payload[..NonceSizeBytes];
        var tag = payload[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var ciphertext = payload[(NonceSizeBytes + TagSizeBytes)..];
        var plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(masterKey, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private string WrapDataKey(byte[] dataKeyPlaintext)
    {
        var masterKey = GetMasterKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[dataKeyPlaintext.Length];

        using var aesGcm = new AesGcm(masterKey, TagSizeBytes);
        aesGcm.Encrypt(nonce, dataKeyPlaintext, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, nonce.Length);
        ciphertext.CopyTo(payload, nonce.Length + tag.Length);

        return Convert.ToBase64String(payload);
    }

    private byte[] GetMasterKey()
    {
        if (string.IsNullOrWhiteSpace(cipherOptions.MasterKeyBase64))
        {
            throw new InvalidOperationException(
                "Integrations:CredentialCipher:MasterKeyBase64 deve estar configurada.");
        }

        var key = Convert.FromBase64String(cipherOptions.MasterKeyBase64);
        if (key.Length != DataKeySizeBytes)
        {
            throw new InvalidOperationException(
                "Integrations:CredentialCipher:MasterKeyBase64 deve representar 32 bytes (AES-256).");
        }

        return key;
    }
}
