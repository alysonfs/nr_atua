using System.Net.Mail;
using Atua.Api.Domain;
using Atua.Api.Domain.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Atua.Api.Application.Identity;

public sealed class SignUpService(
    AtuaDbContext dbContext,
    ISecretHasher secretHasher,
    IEmailConfirmationCodeGenerator codeGenerator,
    IEmailConfirmationSender emailConfirmationSender,
    TimeProvider timeProvider,
    ILogger<SignUpService> logger)
{
    private const int MinimumPasswordLength = 8;
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(15);

    public async Task<SignUpResult> ExecuteAsync(SignUpCommand command,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(command.Email);

        if (!IsValidEmail(email))
        {
            return new SignUpResult(ESignUpStatus.InvalidEmail);
        }

        if (command.Password.Length < MinimumPasswordLength ||
            command.Password != command.PasswordConfirmation)
        {
            return new SignUpResult(ESignUpStatus.InvalidPassword);
        }

        var now = timeProvider.GetUtcNow();
        var existingUser = await dbContext.Users
            .SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (existingUser is not null)
        {
            if (existingUser.EmailConfirmedAt is not null)
            {
                return new SignUpResult(ESignUpStatus.EmailAlreadyRegistered);
            }

            // Cadastro pendente: reenvia a confirmacao sem alterar a senha ja armazenada.
            return await ReissueConfirmationAsync(existingUser, email, now, cancellationToken);
        }

        var code = codeGenerator.Generate();
        var user = new User(Uuid7.New(), null, email, secretHasher.Hash(command.Password));
        var confirmation = new EmailConfirmation(Uuid7.New(), user.Id,
            secretHasher.Hash(code), now.Add(ConfirmationLifetime));

        dbContext.Add(user);
        dbContext.Add(confirmation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueEmailViolation(exception))
        {
            return new SignUpResult(ESignUpStatus.EmailAlreadyRegistered);
        }

        await TrySendConfirmationAsync(email, code, cancellationToken);

        return new SignUpResult(ESignUpStatus.Success, confirmation.Id);
    }

    // A conta e o código já foram persistidos: uma falha de envio (ex.: SMTP fora do ar) não deve
    // reverter o cadastro nem mentir para o usuário; ele pode pedir reenvio depois (ResendConfirmationAsync).
    private async Task TrySendConfirmationAsync(string email, string code, CancellationToken cancellationToken)
    {
        try
        {
            await emailConfirmationSender.SendAsync(email, code, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Falha ao enviar e-mail de confirmação para {Email}", email);
        }
    }

    /// <summary>
    /// Reenvia o código de confirmação para um cadastro pendente. Não revela se o e-mail existe
    /// ou já foi confirmado (evita enumeração de contas).
    /// </summary>
    public async Task ResendConfirmationAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = await dbContext.Users
            .SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || user.EmailConfirmedAt is not null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        await ReissueConfirmationAsync(user, normalizedEmail, now, cancellationToken);
    }

    private async Task<SignUpResult> ReissueConfirmationAsync(User user, string email,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pendingConfirmations = await dbContext.EmailConfirmations
            .Where(confirmation => confirmation.UserId == user.Id && confirmation.ConfirmedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var pendingConfirmation in pendingConfirmations)
        {
            pendingConfirmation.Invalidate(now);
        }

        var code = codeGenerator.Generate();
        var confirmation = new EmailConfirmation(Uuid7.New(), user.Id,
            secretHasher.Hash(code), now.Add(ConfirmationLifetime));

        dbContext.Add(confirmation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueEmailViolation(exception))
        {
            return new SignUpResult(ESignUpStatus.EmailAlreadyRegistered);
        }

        await TrySendConfirmationAsync(email, code, cancellationToken);

        return new SignUpResult(ESignUpStatus.Success, confirmation.Id);
    }

    private static bool IsUniqueEmailViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email);

            return address.Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}