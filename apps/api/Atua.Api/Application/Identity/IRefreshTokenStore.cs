namespace Atua.Api.Application.Identity;

public interface IRefreshTokenStore
{
    Task<bool> TryConsumeAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken);
}
