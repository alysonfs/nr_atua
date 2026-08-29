namespace Atua.Api.Domain.Integrations;

public sealed class Integration
{
    private Integration()
    {
    }

    public Integration(Guid id, Guid tenantId, Guid providerId, bool isEnabled)
    {
        Id = id;
        TenantId = tenantId;
        ProviderId = providerId;
        IsEnabled = isEnabled;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProviderId { get; private set; }

    public bool IsEnabled { get; private set; }
}