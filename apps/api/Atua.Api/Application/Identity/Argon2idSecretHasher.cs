using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Atua.Api.Application.Identity;

public sealed class Argon2idSecretHasher : ISecretHasher
{
    private const int Iterations = 3;
    private const int MemorySize = 32 * 1024;
    private const int Parallelism = 1;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = DeriveKey(value, salt);

        return string.Join('$', "argon2id", "v1", Iterations, MemorySize,
            Parallelism, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string value, string hash)
    {
        var parts = hash.Split('$');

        if (parts.Length != 7 || parts[0] != "argon2id" || parts[1] != "v1" ||
            !int.TryParse(parts[2], out var iterations) ||
            !int.TryParse(parts[3], out var memorySize) ||
            !int.TryParse(parts[4], out var parallelism))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[5]);
            var expectedHash = Convert.FromBase64String(parts[6]);
            var actualHash = DeriveKey(value, salt, iterations, memorySize, parallelism,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] DeriveKey(string value, byte[] salt, int iterations = Iterations,
        int memorySize = MemorySize, int parallelism = Parallelism, int hashSize = HashSize)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(value))
        {
            Salt = salt,
            Iterations = iterations,
            MemorySize = memorySize,
            DegreeOfParallelism = parallelism
        };

        return argon2.GetBytes(hashSize);
    }
}