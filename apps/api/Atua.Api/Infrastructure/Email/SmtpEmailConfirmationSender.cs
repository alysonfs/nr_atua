using System.Net.Mail;
using Atua.Api.Application.Identity;

namespace Atua.Api.Infrastructure.Email;

/// <summary>
/// Implementação de desenvolvimento de <see cref="IEmailConfirmationSender"/> que envia
/// via SMTP para um servidor local (ex.: Mailpit, ver docker-compose.yml). Permite inspecionar
/// o e-mail real (assunto/corpo) numa UI web em vez de apenas ler o código no log.
/// Nunca deve ser usada em produção — o registro condicional em Program.cs garante isso.
/// </summary>
public sealed class SmtpEmailConfirmationSender(string host, int port, string senderAddress)
    : IEmailConfirmationSender
{
    public async Task SendAsync(string email, string code, CancellationToken cancellationToken)
    {
        using var message = new MailMessage(senderAddress, email)
        {
            Subject = "Confirme seu e-mail no ATUA",
            Body = $"Seu codigo de confirmacao e: {code}. Ele expira em 15 minutos."
        };

        using var client = new SmtpClient(host, port);
        await client.SendMailAsync(message, cancellationToken);
    }
}
