using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Atua.Api.Application.Identity;

namespace Atua.Api.Infrastructure.Email;

public sealed class SesEmailConfirmationSender(
    IAmazonSimpleEmailServiceV2 client,
    string senderAddress) : IEmailConfirmationSender
{
    public Task SendAsync(string email, string code, CancellationToken cancellationToken)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = senderAddress,
            Destination = new Destination
            {
                ToAddresses = [email]
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = "Confirme seu e-mail no ATUA" },
                    Body = new Body
                    {
                        Text = new Content
                        {
                            Data = $"Seu codigo de confirmacao e: {code}. Ele expira em 15 minutos."
                        }
                    }
                }
            }
        };

        return client.SendEmailAsync(request, cancellationToken);
    }
}