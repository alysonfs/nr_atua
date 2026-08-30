using Atua.Api.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

public class AesGcmCredentialCipherTests
{
    [Fact]
    public void CifraEDecifraCorretamente()
    {
        var cipher = CreateCipher();
        var dataKey = cipher.CreateDataKey();

        var result = cipher.Encrypt(dataKey.Plaintext, "segredo-super-secreto");
        var decrypted = cipher.Decrypt(dataKey.Plaintext, result.CiphertextBase64, result.Nonce, result.Tag);

        Assert.Equal("segredo-super-secreto", decrypted);
        Assert.DoesNotContain("segredo", result.CiphertextBase64);
    }

    [Fact]
    public void WrapEUnwrapDaChaveDeDadosPreservamOValorOriginal()
    {
        var cipher = CreateCipher();
        var dataKey = cipher.CreateDataKey();

        var unwrapped = cipher.UnwrapDataKey(dataKey.CiphertextBase64, dataKey.KmsKeyId);

        Assert.Equal(dataKey.Plaintext, unwrapped);
    }

    private static AesGcmCredentialCipher CreateCipher() => new(Options.Create(
        new CredentialCipherOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(new byte[32]),
            KmsKeyId = Guid.NewGuid(),
            AlgorithmVersion = 1
        }));
}
