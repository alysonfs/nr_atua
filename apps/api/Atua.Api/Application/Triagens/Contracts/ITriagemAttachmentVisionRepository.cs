using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Acesso de leitura/escrita a <see cref="TriagemInboundMedia"/> necessário
/// à classificação de visão computacional (decisão do PO). Separado de
/// <see cref="ITriagemInboundMediaRepository"/> (que cobre apenas a
/// ingestão) porque representa um caso de uso distinto, com ciclo de vida
/// próprio (classifica depois que a mídia já foi ingerida e armazenada).
/// </summary>
public interface ITriagemAttachmentVisionRepository
{
    /// <summary>
    /// Imagens e documentos PDF já armazenados (<see cref="ETriagemInboundMediaOutcome.ColetaIniciada"/>
    /// ou <see cref="ETriagemInboundMediaOutcome.Armazenada"/>) desta
    /// triagem — inclui tanto as ainda não classificadas quanto as já
    /// classificadas em turnos anteriores (necessário para reaplicar a
    /// regra determinística do checklist a cada turno, de forma idempotente).
    /// Entidades tracked pelo mesmo <c>DbContext</c> scoped do chamador.
    /// </summary>
    Task<IReadOnlyList<TriagemInboundMedia>> FindClassifiableMediaAsync(Guid triagemId, CancellationToken cancellationToken);

    /// <summary>Persiste classificações aplicadas em memória via <see cref="TriagemInboundMedia.RegistrarClassificacaoVisao"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
