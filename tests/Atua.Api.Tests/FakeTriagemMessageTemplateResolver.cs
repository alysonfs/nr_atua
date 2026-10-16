using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

internal sealed class FakeTriagemMessageTemplateResolver : ITriagemMessageTemplateResolver
{
    public static readonly Guid TemplateVersionId = Guid.Parse("0199dd69-d292-7ae1-a627-14804ae99892");

    public Task<ResolvedTriagemMessageTemplate?> ResolveInitialContactAsync(
        Guid tenantId,
        Guid integrationId,
        string workOrderProviderId,
        string? workOrderProviderNo,
        string? productBrand,
        string? productModel,
        CancellationToken cancellationToken) =>
        Task.FromResult<ResolvedTriagemMessageTemplate?>(
            new ResolvedTriagemMessageTemplate(
                TemplateVersionId,
                ETriagemMessageTemplateType.InitialContact,
                1,
                $"Mensagem renderizada para {workOrderProviderId}"));
}
