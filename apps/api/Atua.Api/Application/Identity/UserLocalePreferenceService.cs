using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Identity;

public sealed class UserLocalePreferenceService(AtuaDbContext dbContext)
{
    public Task<string?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.PreferredLocale)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> UpdateAsync(Guid userId, string? locale,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            item => item.Id == userId, cancellationToken);
        if (user is null) return false;

        user.SetPreferredLocale(locale);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
