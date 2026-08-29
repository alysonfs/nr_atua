namespace Atua.Api.Domain.Identity;

public sealed class AuthRefreshToken
{
    private AuthRefreshToken() { }

    public AuthRefreshToken(Guid id, Guid sessionId, Guid familyId, string tokenHash,
        DateTimeOffset expiresAt)
    {
        Id = id; SessionId = sessionId; FamilyId = familyId; TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    public void MarkUsed(DateTimeOffset now) => UsedAt ??= now;
}
