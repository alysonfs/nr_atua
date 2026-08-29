using Atua.Api.Domain.Tenants;

namespace Atua.Api.Domain.Identity;

public sealed class AuthSession
{
    private AuthSession()
    {
    }

    public AuthSession(Guid id, Guid userId, DateTimeOffset createdAt, string? timeZoneOverrideId = null)
    {
        Id = id;
        UserId = userId;
        CreatedAt = createdAt;
        SetTimeZoneOverride(timeZoneOverrideId);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public string? TimeZoneOverrideId { get; private set; }

    public void SetTimeZoneOverride(string? timeZoneOverrideId)
    {
        if (timeZoneOverrideId is not null && !IanaTimeZone.IsValid(timeZoneOverrideId))
        {
            throw new ArgumentException("Identificador de fuso horário IANA inválido.", nameof(timeZoneOverrideId));
        }

        TimeZoneOverrideId = timeZoneOverrideId;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
