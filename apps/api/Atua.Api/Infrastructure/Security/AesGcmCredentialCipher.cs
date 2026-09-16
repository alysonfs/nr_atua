using System.Security.Cryptography;

namespace Atua.Api.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="ICredentialCipher"/> usando AES-256-GCM local
/// (AlgorithmVersion=1, ADR-004/ADR-018). Usada em desenvolvimento e testes,
/// e para decifrar registros v1 legados quando a implementação KMS estiver ativa.
///
/// A chave mestra nunca deve ser versionada — configure via
/// <c>dotnet user-secrets</c> em desenvolvimento.
/// </summary>
public sealed class AesGcmCredentialCipher(
    Microsoft.Extensions.Options.IOptions<CredentialCipherOptions> options) : ICredentialCipher
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int DataKeySizeBytes = 32; // AES-256
    private const int AlgorithmVersion = 1;
    private readonly CredentialCipherOptions cipherOptions = options.Value;

    public Task<EncryptedDataKey> CreateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        var dataKeyPlaintext = RandomNumberGenerator.GetBytes(DataKeySizeBytes);
        var wrapped = WrapDataKey(dataKeyPlaintext);
        // KmsKeyId para v1 é o valor de configuração (opaco, não um ARN real).
        var keyId = string.IsNullOrWhiteSpace(cipherOptions.KmsKeyArn)
            ? "local-v1"
            : cipherOptions.KmsKeyArn;
        return Task.FromResult(new EncryptedDataKey(dataKeyPlaintext, wrapped, keyId, AlgorithmVersion));
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

    public Task<byte[]> UnwrapDataKeyAsync(string dataKeyCiphertextBase64, string kmsKeyId,
        int algorithmVersion, CancellationToken cancellationToken = default)
    {
        // v1: unwrap local com AES-GCM usando a chave mestra de configuração.
        var masterKey = GetMasterKey();
        var payload = Convert.FromBase64String(dataKeyCiphertextBase64);
        var nonce = payload[..NonceSizeBytes];
        var tag = payload[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var ciphertext = payload[(NonceSizeBytes + TagSizeBytes)..];
        var plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(masterKey, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return Task.FromResult(plaintext);
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
                "Integrations:CredentialCipher:MasterKeyBase64 deve estar configurada para " +
                "AlgorithmVersion=1. Configure via 'dotnet user-secrets set " +
                "\"Integrations:CredentialCipher:MasterKeyBase64\" \"$(openssl rand -base64 32)\"'.");
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
