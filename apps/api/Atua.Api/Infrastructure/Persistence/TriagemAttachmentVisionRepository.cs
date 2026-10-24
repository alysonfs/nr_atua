using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Implementação EF Core de <see cref="ITriagemAttachmentVisionRepository"/>.
/// As entidades retornadas por <see cref="FindClassifiableMediaAsync"/> são
/// tracked pelo mesmo <c>DbContext</c> scoped do chamador — mutações feitas
/// via <see cref="TriagemInboundMedia.RegistrarClassificacaoVisao"/> são
/// persistidas por <see cref="SaveChangesAsync"/>, mesmo princípio já usado
/// por <c>ITriagemChannelCommandRepository</c>/<c>ITriagemRepository</c>.
/// </summary>
public sealed class TriagemAttachmentVisionRepository(AtuaDbContext dbContext) : ITriagemAttachmentVisionRepository
{
    public async Task<IReadOnlyList<TriagemInboundMedia>> FindClassifiableMediaAsync(Guid triagemId,
        CancellationToken cancellationToken) =>
        await dbContext.TriagemInboundMedia
            .Where(media => media.TriagemId == triagemId
                && (media.MediaKind == ETriagemInboundMediaKind.Image
                    || (media.MediaKind == ETriagemInboundMediaKind.Document && media.ContentType == "application/pdf"))
                && (media.Outcome == ETriagemInboundMediaOutcome.ColetaIniciada
                    || media.Outcome == ETriagemInboundMediaOutcome.Armazenada))
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
