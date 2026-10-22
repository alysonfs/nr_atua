namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Tipos de mídia aceitos na ingestão inbound da triagem (incremento de
/// mídia). Figurinhas e demais documentos (além de PDF) estão fora de
/// escopo e nunca são armazenados — apenas PDF é aceito como documento.
/// </summary>
public enum ETriagemInboundMediaKind
{
    Audio,
    Image,
    Video,
    Document
}
