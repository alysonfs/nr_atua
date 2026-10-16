using System.Text.RegularExpressions;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Triagens;

public sealed class TriagemMessageTemplateRenderer(AtuaDbContext dbContext) : ITriagemMessageTemplateResolver
{
    private static readonly Regex PlaceholderRegex = new(
        """\{\{\s*(?<name>[^{}]+?)\s*\}\}""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<ETriagemMessageTemplateType, HashSet<string>> Allowed =
        new Dictionary<ETriagemMessageTemplateType, HashSet<string>>
        {
            [ETriagemMessageTemplateType.InitialContact] =
                ["nome_posto", "fabricante", "numero_os", "produto"],
            [ETriagemMessageTemplateType.Reminder24Hours] =
                ["numero_os", "itens_pendentes"],
            [ETriagemMessageTemplateType.Reminder36Hours] =
                ["numero_os", "itens_pendentes"]
        };

    public static IReadOnlyList<string> Validate(ETriagemMessageTemplateType type, string content)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            errors.Add("template_content_required");
            return errors;
        }

        if (content.Length > 8_000) errors.Add("template_content_too_long");

        var placeholders = PlaceholderRegex.Matches(content)
            .Select(match => match.Groups["name"].Value.Trim())
            .ToHashSet(StringComparer.Ordinal);

        var withoutValidPairs = PlaceholderRegex.Replace(content, string.Empty);
        if (withoutValidPairs.Contains("{{", StringComparison.Ordinal)
            || withoutValidPairs.Contains("}}", StringComparison.Ordinal))
        {
            errors.Add("malformed_placeholder");
        }

        foreach (var placeholder in placeholders.Where(item => !Allowed[type].Contains(item)))
        {
            errors.Add($"unknown_placeholder:{placeholder}");
        }

        foreach (var required in Allowed[type].Where(item => !placeholders.Contains(item)))
        {
            errors.Add($"required_placeholder_missing:{required}");
        }

        return errors;
    }

    public static string Render(string content, IReadOnlyDictionary<string, string> values) =>
        PlaceholderRegex.Replace(content, match =>
        {
            var name = match.Groups["name"].Value.Trim();
            return values.TryGetValue(name, out var value) ? value : match.Value;
        });

    public static string RenderPreview(ETriagemMessageTemplateType type, string content) =>
        Render(content, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nome_posto"] = "Natal Refrigeração",
            ["fabricante"] = "Midea",
            ["numero_os"] = "BRWO260909869",
            ["produto"] = "Refrigerador modelo XPTO",
            ["itens_pendentes"] = "• foto legível da nota fiscal\n• endereço completo"
        });

    public async Task<ResolvedTriagemMessageTemplate?> ResolveInitialContactAsync(
        Guid tenantId, Guid integrationId, string workOrderProviderId, string? workOrderProviderNo,
        string? productBrand, string? productModel, CancellationToken cancellationToken)
    {
        var template = await dbContext.TriagemMessageTemplates.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                    item.TenantId == tenantId
                    && item.IntegrationId == integrationId
                    && item.Type == ETriagemMessageTemplateType.InitialContact
                    && item.Status == ETriagemMessageTemplateStatus.Published
                    && item.IsActive,
                cancellationToken);
        if (template is null) return null;

        var identity = await (
            from integration in dbContext.Integrations.AsNoTracking()
            join tenant in dbContext.Tenants.AsNoTracking() on integration.TenantId equals tenant.Id
            join provider in dbContext.IntegrationProviders.AsNoTracking()
                on integration.ProviderId equals provider.Id
            where integration.Id == integrationId && integration.TenantId == tenantId
            select new { TenantName = tenant.Name, provider.Manufacturer })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null) return null;

        var product = string.Join(' ', new[] { productBrand, productModel }
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
        if (string.IsNullOrWhiteSpace(product)) product = "seu produto";

        var rendered = Render(template.Content, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nome_posto"] = identity.TenantName,
            ["fabricante"] = identity.Manufacturer,
            ["numero_os"] = string.IsNullOrWhiteSpace(workOrderProviderNo)
                ? workOrderProviderId
                : workOrderProviderNo,
            ["produto"] = product
        });

        return new ResolvedTriagemMessageTemplate(template.Id, template.Type, template.Version, rendered);
    }
}
