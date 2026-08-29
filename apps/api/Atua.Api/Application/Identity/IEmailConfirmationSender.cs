namespace Atua.Api.Application.Identity;

public interface IEmailConfirmationSender
{
    Task SendAsync(string email, string code, CancellationToken cancellationToken);
}