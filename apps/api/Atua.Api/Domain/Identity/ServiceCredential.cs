namespace Atua.Api.Domain.Identity;

public sealed class ServiceCredential
{
    private ServiceCredential() { }

    public ServiceCredential(Guid id, Guid tenantId, Guid integrationId, Guid providerId,
        string tokenHash, string scope)
    {
        Id = id; TenantId = tenantId; IntegrationId = integrationId; ProviderId = providerId;
        TokenHash = tokenHash; Scope = scope;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid IntegrationId { get; private set; }
    public Guid ProviderId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public string Scope { get; private set; } = null!;
    public DateTimeOffset? RevokedAt { get; private set; }
}
