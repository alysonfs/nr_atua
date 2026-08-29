using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Identity;

public sealed class ConfirmEmailService(
    AtuaDbContext dbContext,
    ISecretHasher secretHasher,
    TimeProvider timeProvider)
{
    public async Task<ConfirmEmailResult> ExecuteAsync(ConfirmEmailCommand command,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(command.Email);

        var user = await dbContext.Users
            .SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (user is null)
        {
            return new ConfirmEmailResult(EConfirmEmailStatus.InvalidCode);
        }

        if (user.EmailConfirmedAt is not null)
        {
            return new ConfirmEmailResult(EConfirmEmailStatus.AlreadyConfirmed);
        }

        var pendingConfirmation = await dbContext.EmailConfirmations
            .Where(confirmation => confirmation.UserId == user.Id && confirmation.ConfirmedAt == null)
            .OrderByDescending(confirmation => confirmation.ExpiresAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (pendingConfirmation is null || !secretHasher.Verify(command.Code, pendingConfirmation.CodeHash))
        {
            return new ConfirmEmailResult(EConfirmEmailStatus.InvalidCode);
        }

        var now = timeProvider.GetUtcNow();

        try
        {
            pendingConfirmation.Confirm(now);
        }
        catch (InvalidOperationException)
        {
            return new ConfirmEmailResult(EConfirmEmailStatus.ExpiredCode);
        }

        user.ConfirmEmail(now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ConfirmEmailResult(EConfirmEmailStatus.Success);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
