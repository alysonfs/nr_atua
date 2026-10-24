using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Fake em memória de <see cref="ITriagemAttachmentVisionRepository"/> para
/// testar <c>ClassifyTriagemAttachmentsUseCase</c> sem PostgreSQL real —
/// mesmo padrão de <see cref="FakeTriagemLlmUsageRepository"/>. As mídias
/// são mantidas em uma lista única (sem tracking real de EF Core), mas as
/// mutações aplicadas pelo caso de uso via
/// <see cref="TriagemInboundMedia.RegistrarClassificacaoVisao"/> são sobre
/// as mesmas instâncias devolvidas por <see cref="FindClassifiableMediaAsync"/>.
/// </summary>
internal sealed class FakeTriagemAttachmentVisionRepository : ITriagemAttachmentVisionRepository
{
    private readonly List<TriagemInboundMedia> media = [];

    public int SaveChangesCallCount { get; private set; }

    public void Add(TriagemInboundMedia item) => media.Add(item);

    public Task<IReadOnlyList<TriagemInboundMedia>> FindClassifiableMediaAsync(Guid triagemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TriagemInboundMedia> result =
            media.Where(item => item.TriagemId == triagemId
                && (item.MediaKind == ETriagemInboundMediaKind.Image
                    || (item.MediaKind == ETriagemInboundMediaKind.Document && item.ContentType == "application/pdf"))
                && (item.Outcome == ETriagemInboundMediaOutcome.ColetaIniciada
                    || item.Outcome == ETriagemInboundMediaOutcome.Armazenada)).ToList();
        return Task.FromResult(result);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
