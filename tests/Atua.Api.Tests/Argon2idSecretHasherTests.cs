using Atua.Api.Application.Identity;

namespace Atua.Api.Tests;

public class Argon2idSecretHasherTests
{
    [Fact]
    public void GeraEVerificaHashArgon2id()
    {
        var hasher = new Argon2idSecretHasher();
        var hash = hasher.Hash("senha123");

        Assert.StartsWith("argon2id$v1$", hash);
        Assert.True(hasher.Verify("senha123", hash));
        Assert.False(hasher.Verify("senha-incorreta", hash));
    }
}