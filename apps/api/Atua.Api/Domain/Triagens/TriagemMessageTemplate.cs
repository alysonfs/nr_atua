namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Versão de um template de mensagem da triagem. Versões publicadas são
/// imutáveis; alterações posteriores sempre criam uma nova versão.
/// </summary>
public sealed class TriagemMessageTemplate
{
    private TriagemMessageTemplate()
    {
    }

    private TriagemMessageTemplate(Guid id, Guid tenantId, Guid integrationId,
        ETriagemMessageTemplateType type, int version, string content,
        ETriagemMessageTemplateStatus status, bool isActive, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        Type = type;
        Version = version;
        Content = NormalizeContent(content);
        Status = status;
        IsActive = isActive;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        PublishedAtUtc = status == ETriagemMessageTemplateStatus.Published ? now : null;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid IntegrationId { get; private set; }

    public ETriagemMessageTemplateType Type { get; private set; }

    public int Version { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public ETriagemMessageTemplateStatus Status { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static TriagemMessageTemplate CreateDraft(Guid id, Guid tenantId, Guid integrationId,
        ETriagemMessageTemplateType type, int version, string content, DateTimeOffset now)
    {
        ValidateIdentity(tenantId, integrationId, version);
        return new TriagemMessageTemplate(id, tenantId, integrationId, type, version, content,
            ETriagemMessageTemplateStatus.Draft, isActive: false, now);
    }

    public static TriagemMessageTemplate CreatePublished(Guid id, Guid tenantId, Guid integrationId,
        ETriagemMessageTemplateType type, int version, string content, bool isActive, DateTimeOffset now)
    {
        ValidateIdentity(tenantId, integrationId, version);
        return new TriagemMessageTemplate(id, tenantId, integrationId, type, version, content,
            ETriagemMessageTemplateStatus.Published, isActive, now);
    }

    public void UpdateDraft(string content, DateTimeOffset now)
    {
        EnsureDraft(nameof(UpdateDraft));
        Content = NormalizeContent(content);
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Publish(DateTimeOffset now)
    {
        EnsureDraft(nameof(Publish));
        Status = ETriagemMessageTemplateStatus.Published;
        IsActive = true;
        PublishedAtUtc = now;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (Status != ETriagemMessageTemplateStatus.Published)
        {
            throw new InvalidOperationException("Somente versões publicadas podem ser desativadas.");
        }

        if (!IsActive) return;

        IsActive = false;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    private void EnsureDraft(string operation)
    {
        if (Status != ETriagemMessageTemplateStatus.Draft)
        {
            throw new InvalidOperationException(
                $"{operation} exige um template em rascunho; versões publicadas são imutáveis.");
        }
    }

    private static void ValidateIdentity(Guid tenantId, Guid integrationId, int version)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (integrationId == Guid.Empty)
            throw new ArgumentException("IntegrationId não pode ser vazio.", nameof(integrationId));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
    }

    private static string NormalizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("O conteúdo do template não pode ser vazio.", nameof(content));
        }

        var normalized = content.Trim();
        if (normalized.Length > 8_000)
        {
            throw new ArgumentException("O conteúdo do template não pode exceder 8.000 caracteres.",
                nameof(content));
        }

        return normalized;
    }
}
