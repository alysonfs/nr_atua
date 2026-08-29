namespace Atua.Api.Application.Identity;

public interface IEmailConfirmationCodeGenerator
{
    string Generate();
}