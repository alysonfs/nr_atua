namespace Atua.Api.Application.Identity;

public interface ISecretHasher
{
    string Hash(string value);

    bool Verify(string value, string hash);
}