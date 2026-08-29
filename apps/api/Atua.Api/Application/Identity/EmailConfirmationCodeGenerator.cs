using System.Globalization;
using System.Security.Cryptography;

namespace Atua.Api.Application.Identity;

public sealed class EmailConfirmationCodeGenerator : IEmailConfirmationCodeGenerator
{
    public string Generate() => RandomNumberGenerator.GetInt32(100_000, 1_000_000)
        .ToString(CultureInfo.InvariantCulture);
}