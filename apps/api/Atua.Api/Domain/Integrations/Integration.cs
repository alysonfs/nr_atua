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
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProviderId { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? LastCollectionAtUtc { get; private set; }

    public void Enable(DateTimeOffset now)
    {
        IsEnabled = true;
        UpdatedAtUtc = now;
    }

    public void Disable(DateTimeOffset now)
    {
        IsEnabled = false;
        UpdatedAtUtc = now;
    }

    /// <summary>Registra o instante da última coleta bem-sucedida (ADR-024).</summary>
    public void RecordCollection(DateTimeOffset now)
    {
        LastCollectionAtUtc = now;
        UpdatedAtUtc = now;
    }
}