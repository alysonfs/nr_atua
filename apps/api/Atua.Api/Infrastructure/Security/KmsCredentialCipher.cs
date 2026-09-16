using System.Security.Cryptography;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Microsoft.Extensions.Options;

namespace Atua.Api.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="ICredentialCipher"/> usando AWS KMS para
/// wrap/unwrap da DEK (AlgorithmVersion=2, ADR-004/ADR-018/D9).
///
/// A chave mestra NUNCA sai do KMS: <c>GenerateDataKey</c> produz a DEK em
/// claro (usada localmente para AES-256-GCM) e a DEK cifrada (armazenada).
/// O unwrap usa <c>Decrypt</c> do KMS. A cifragem dos campos permanece
/// AES-256-GCM local com a DEK.
///
/// Registros AlgorithmVersion=1 são delegados ao <see cref="AesGcmCredentialCipher"/>
/// injetado, garantindo coexistência sem re-wrap.
/// </summary>
public sealed class KmsCredentialCipher(
    IAmazonKeyManagementService kmsClient,
    AesGcmCredentialCipher localCipher,
    IOptions<CredentialCipherOptions> options) : ICredentialCipher
{
    private const int AlgorithmVersion = 2;
    private readonly CredentialCipherOptions cipherOptions = options.Value;

    public async Task<EncryptedDataKey> CreateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        var keyArn = RequireKeyArn();

        var request = new GenerateDataKeyRequest
        {
            KeyId = keyArn,
            KeySpec = DataKeySpec.AES_256
        };

        var response = await kmsClient.GenerateDataKeyAsync(request, cancellationToken);

        var plaintext = response.Plaintext.ToArray();
        var ciphertextBase64 = Convert.ToBase64String(response.CiphertextBlob.ToArray());

        return new EncryptedDataKey(plaintext, ciphertextBase64, keyArn, AlgorithmVersion);
    }

    public CipherResult Encrypt(byte[] dataKeyPlaintext, string plaintext)
        => localCipher.Encrypt(dataKeyPlaintext, plaintext);

    public string Decrypt(byte[] dataKeyPlaintext, string ciphertextBase64, byte[] nonce, byte[] tag)
        => localCipher.Decrypt(dataKeyPlaintext, ciphertextBase64, nonce, tag);

    public async Task<byte[]> UnwrapDataKeyAsync(string dataKeyCiphertextBase64, string kmsKeyId,
        int algorithmVersion, CancellationToken cancellationToken = default)
    {
        if (algorithmVersion == 1)
        {
            // Coexistência: registro antigo — delega ao cipher local (v1).
            return await localCipher.UnwrapDataKeyAsync(
                dataKeyCiphertextBase64, kmsKeyId, algorithmVersion, cancellationToken);
        }

        // v2: unwrap via KMS.
        var ciphertextBytes = Convert.FromBase64String(dataKeyCiphertextBase64);
        var request = new DecryptRequest
        {
            CiphertextBlob = new MemoryStream(ciphertextBytes),
            KeyId = kmsKeyId
        };

        var response = await kmsClient.DecryptAsync(request, cancellationToken);
        return response.Plaintext.ToArray();
    }

    private string RequireKeyArn()
    {
        if (string.IsNullOrWhiteSpace(cipherOptions.KmsKeyArn))
        {
            throw new InvalidOperationException(
                "Integrations:CredentialCipher:KmsKeyArn deve estar configurado para usar o " +
                "KmsCredentialCipher. Configure via variável de ambiente " +
                "Integrations__CredentialCipher__KmsKeyArn.");
        }

        return cipherOptions.KmsKeyArn;
    }
}
