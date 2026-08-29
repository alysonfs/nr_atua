using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Identity;

public sealed class RefreshTokenStore(AtuaDbContext dbContext) : IRefreshTokenStore
{
    public async Task<bool> TryConsumeAsync(Guid tokenId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            var affected = await dbContext.AuthRefreshTokens
                .Where(token => token.Id == tokenId && token.UsedAt == null && token.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.UsedAt, now),
                    cancellationToken);
            return affected == 1;
        }

        // O provider InMemory não implementa ExecuteUpdate. Este caminho serve
        // somente aos testes locais; PostgreSQL sempre usa o compare-and-set acima.
        var token = await dbContext.AuthRefreshTokens.FindAsync(new object?[] { tokenId },
            cancellationToken);
        if (token is null || token.UsedAt is not null || token.ExpiresAt <= now) return false;
        token.MarkUsed(now);
        return true;
    }
}
