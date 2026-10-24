namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Tipo de anexo detectado pela classificação de visão computacional no
/// Bedrock (decisão do PO de adiantar a conferência automática de nota
/// fiscal e etiqueta via WhatsApp). Aplicado somente a imagens — nunca a
/// vídeo, áudio ou documento (o vídeo do defeito é opcional e nunca
/// interpretado nesta fase).
/// </summary>
public enum ETriagemAttachmentVisionTipo
{
    /// <summary>Nota fiscal — item <see cref="ETriagemChecklistItem.NotaFiscal"/> do checklist.</summary>
    NotaFiscal,

    /// <summary>Etiqueta do produto (modelo + número de série) — item <see cref="ETriagemChecklistItem.Etiqueta"/>.</summary>
    Etiqueta,

    /// <summary>Qualquer outra imagem (ex.: foto do defeito) — não alimenta nenhum item obrigatório do checklist.</summary>
    Outro,
}
