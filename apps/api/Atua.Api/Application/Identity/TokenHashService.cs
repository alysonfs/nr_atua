using System.Security.Cryptography;
using System.Text;

namespace Atua.Api.Application.Identity;

public sealed class TokenHashService : ITokenHashService
{
    public string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
