namespace Atua.Api.Application.Identity;

public interface ITokenHashService
{
    string Hash(string token);
    string GenerateToken();
}
