using Atua.Api.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

public class AesGcmCredentialCipherTests
{
    [Fact]
    public async Task CifraEDecifraCorretamente()
    {
        var cipher = CreateCipher();
        var dataKey = await cipher.CreateDataKeyAsync();

        var result = cipher.Encrypt(dataKey.Plaintext, "segredo-super-secreto");
        var decrypted = cipher.Decrypt(dataKey.Plaintext, result.CiphertextBase64, result.Nonce, result.Tag);

        Assert.Equal("segredo-super-secreto", decrypted);
        Assert.DoesNotContain("segredo", result.CiphertextBase64);
    }

    [Fact]
    public async Task WrapEUnwrapDaChaveDeDadosPreservamOValorOriginal()
    {
        var cipher = CreateCipher();
        var dataKey = await cipher.CreateDataKeyAsync();

        var unwrapped = await cipher.UnwrapDataKeyAsync(
            dataKey.CiphertextBase64, dataKey.KmsKeyId, dataKey.AlgorithmVersion);

        Assert.Equal(dataKey.Plaintext, unwrapped);
    }

    [Fact]
    public async Task AlgorithmVersionEUm()
    {
        var cipher = CreateCipher();
        var dataKey = await cipher.CreateDataKeyAsync();

        Assert.Equal(1, dataKey.AlgorithmVersion);
    }

    private static AesGcmCredentialCipher CreateCipher() => new(Options.Create(
        new CredentialCipherOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(new byte[32])
        }));
}
