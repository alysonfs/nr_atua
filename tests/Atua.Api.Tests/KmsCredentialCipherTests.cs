using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Atua.Api.Infrastructure.Security;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Atua.Api.Tests;

/// <summary>
/// Testes do KmsCredentialCipher (D9/ADR-004):
/// - wrap/unwrap da DEK via KMS mockado;
/// - coexistência v1/v2 (registro antigo continua decifrável pelo cipher local);
/// - fallback quando o ARN não está configurado (usa cipher local, sem KMS).
/// </summary>
public class KmsCredentialCipherTests
{
    // -----------------------------------------------------------------------
    // Wrap/Unwrap via KMS mockado
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateDataKeyAsync_UsaKmsGenerateDataKey_ERetornaEncryptedDataKey()
    {
        var (kmsClient, _, cipher) = CreateKmsCipher();
        var expectedPlaintext = new byte[32];
        var expectedCiphertext = new byte[64];
        Random.Shared.NextBytes(expectedPlaintext);
        Random.Shared.NextBytes(expectedCiphertext);

        kmsClient.GenerateDataKeyAsync(Arg.Any<GenerateDataKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateDataKeyResponse
            {
                Plaintext = new MemoryStream(expectedPlaintext),
                CiphertextBlob = new MemoryStream(expectedCiphertext)
            });

        var result = await cipher.CreateDataKeyAsync();

        Assert.Equal(expectedPlaintext, result.Plaintext);
        Assert.Equal(Convert.ToBase64String(expectedCiphertext), result.CiphertextBase64);
        Assert.Equal("arn:aws:kms:us-east-1:123456789012:key/test-key", result.KmsKeyId);
        Assert.Equal(2, result.AlgorithmVersion);

        await kmsClient.Received(1).GenerateDataKeyAsync(
            Arg.Is<GenerateDataKeyRequest>(r =>
                r.KeyId == "arn:aws:kms:us-east-1:123456789012:key/test-key" &&
                r.KeySpec == DataKeySpec.AES_256),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_V2_UsaKmsDecrypt()
    {
        var (kmsClient, _, cipher) = CreateKmsCipher();
        var expectedDek = new byte[32];
        Random.Shared.NextBytes(expectedDek);
        var ciphertextBlob = new byte[64];
        Random.Shared.NextBytes(ciphertextBlob);

        kmsClient.DecryptAsync(Arg.Any<DecryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptResponse
            {
                Plaintext = new MemoryStream(expectedDek)
            });

        var result = await cipher.UnwrapDataKeyAsync(
            Convert.ToBase64String(ciphertextBlob),
            "arn:aws:kms:us-east-1:123456789012:key/test-key",
            algorithmVersion: 2);

        Assert.Equal(expectedDek, result);

        await kmsClient.Received(1).DecryptAsync(
            Arg.Is<DecryptRequest>(r =>
                r.CiphertextBlob.ToArray().SequenceEqual(ciphertextBlob) &&
                r.KeyId == "arn:aws:kms:us-east-1:123456789012:key/test-key"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Encrypt_EDecrypt_FuncionamComDekDoKms()
    {
        var (kmsClient, _, cipher) = CreateKmsCipher();
        var dekPlaintext = new byte[32];
        Random.Shared.NextBytes(dekPlaintext);
        var dekCiphertext = new byte[64];

        kmsClient.GenerateDataKeyAsync(Arg.Any<GenerateDataKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GenerateDataKeyResponse
            {
                Plaintext = new MemoryStream(dekPlaintext),
                CiphertextBlob = new MemoryStream(dekCiphertext)
            });
        kmsClient.DecryptAsync(Arg.Any<DecryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DecryptResponse
            {
                Plaintext = new MemoryStream(dekPlaintext)
            });

        var dataKey = await cipher.CreateDataKeyAsync();
        var encrypted = cipher.Encrypt(dataKey.Plaintext, "valor-secreto");
        var unwrapped = await cipher.UnwrapDataKeyAsync(
            dataKey.CiphertextBase64, dataKey.KmsKeyId, dataKey.AlgorithmVersion);
        var decrypted = cipher.Decrypt(unwrapped, encrypted.CiphertextBase64, encrypted.Nonce, encrypted.Tag);

        Assert.Equal("valor-secreto", decrypted);
    }

    // -----------------------------------------------------------------------
    // Coexistência v1/v2
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UnwrapDataKeyAsync_V1_DelegaParaCipherLocal_SemChamarKms()
    {
        // Registros antigos (v1) devem ser decifráveis com a chave local,
        // sem nenhuma chamada ao KMS.
        var (kmsClient, localCipher, kmsCipher) = CreateKmsCipher();

        var v1DataKey = await localCipher.CreateDataKeyAsync();

        var result = await kmsCipher.UnwrapDataKeyAsync(
            v1DataKey.CiphertextBase64, v1DataKey.KmsKeyId, algorithmVersion: 1);

        Assert.Equal(v1DataKey.Plaintext, result);
        await kmsClient.DidNotReceive().DecryptAsync(
            Arg.Any<DecryptRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistroV1ContinuaDecifravelApósAtivacaoDoKms()
    {
        // Simula o cenário de coexistência: registro foi criado antes do KMS
        // (v1, cifrado com chave local) e deve continuar decifrável quando o
        // KmsCredentialCipher está ativo.
        var masterKey = new byte[32];
        Random.Shared.NextBytes(masterKey);
        var (_, localCipher, kmsCipher) = CreateKmsCipher(masterKey);

        // Cria e cifra dados usando o cipher local (simula registro v1 existente).
        var v1DataKey = await localCipher.CreateDataKeyAsync();
        var encrypted = localCipher.Encrypt(v1DataKey.Plaintext, "segredo-legado");

        // Decifra via KmsCredentialCipher (deve delegar para o cipher local).
        var unwrapped = await kmsCipher.UnwrapDataKeyAsync(
            v1DataKey.CiphertextBase64, v1DataKey.KmsKeyId, algorithmVersion: 1);
        var decrypted = kmsCipher.Decrypt(unwrapped, encrypted.CiphertextBase64, encrypted.Nonce, encrypted.Tag);

        Assert.Equal("segredo-legado", decrypted);
    }

    // -----------------------------------------------------------------------
    // Fallback quando KmsKeyArn não está configurado
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SemKmsKeyArn_CreateDataKeyAsync_UsaCipherLocal()
    {
        // Quando não há ARN configurado, o DI registra AesGcmCredentialCipher
        // diretamente como ICredentialCipher. Testamos o comportamento do
        // AesGcmCredentialCipher nesse cenário (sem KMS).
        var localCipher = CreateLocalCipher();

        var dataKey = await localCipher.CreateDataKeyAsync();

        Assert.Equal(1, dataKey.AlgorithmVersion);
        Assert.Equal(32, dataKey.Plaintext.Length);
    }

    [Fact]
    public async Task KmsCredentialCipher_SemArnConfigurado_LancaInvalidOperationException()
    {
        // Se por algum motivo o KmsCredentialCipher for instanciado sem ARN
        // (situação de misconfiguration), deve lançar exceção clara.
        var kmsClient = Substitute.For<IAmazonKeyManagementService>();
        var localCipher = CreateLocalCipher();
        var options = Options.Create(new CredentialCipherOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(new byte[32]),
            KmsKeyArn = string.Empty // ARN ausente
        });
        var cipher = new KmsCredentialCipher(kmsClient, (AesGcmCredentialCipher)localCipher, options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cipher.CreateDataKeyAsync());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (IAmazonKeyManagementService KmsClient,
        AesGcmCredentialCipher LocalCipher,
        KmsCredentialCipher KmsCipher) CreateKmsCipher(byte[]? masterKey = null)
    {
        var key = masterKey ?? new byte[32];
        var options = Options.Create(new CredentialCipherOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(key),
            KmsKeyArn = "arn:aws:kms:us-east-1:123456789012:key/test-key"
        });
        var kmsClient = Substitute.For<IAmazonKeyManagementService>();
        var localCipher = new AesGcmCredentialCipher(options);
        var kmsCipher = new KmsCredentialCipher(kmsClient, localCipher, options);
        return (kmsClient, localCipher, kmsCipher);
    }

    private static ICredentialCipher CreateLocalCipher() => new AesGcmCredentialCipher(Options.Create(
        new CredentialCipherOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(new byte[32])
        }));
}
