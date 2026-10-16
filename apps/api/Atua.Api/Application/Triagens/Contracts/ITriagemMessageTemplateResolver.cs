using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

public interface ITriagemMessageTemplateResolver
{
    Task<ResolvedTriagemMessageTemplate?> ResolveInitialContactAsync(
        Guid tenantId,
        Guid integrationId,
        string workOrderProviderId,
        string? workOrderProviderNo,
        string? productBrand,
        string? productModel,
        CancellationToken cancellationToken);
}

public sealed record ResolvedTriagemMessageTemplate(
    Guid TemplateVersionId,
    ETriagemMessageTemplateType Type,
    int Version,
    string RenderedContent);
