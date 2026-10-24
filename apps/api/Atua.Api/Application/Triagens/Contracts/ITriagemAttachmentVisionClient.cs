using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Contrato que não depende de fornecedor para a classificação de visão
/// computacional de anexos de imagem da triagem (decisão do PO de adiantar
/// a conferência automática de nota fiscal e etiqueta via WhatsApp, antes
/// do motor JEV completo). Aplicado a imagens e PDF — nunca a vídeo ou
/// áudio; o vídeo do defeito é opcional e nunca interpretado
/// nesta fase.
///
/// Reaproveita <see cref="ETriagemLlmOutcome"/> e <see cref="TriagemLlmUsageInfo"/>
/// de <see cref="ITriagemLlmClient"/>: classificação de anexo é apenas mais
/// uma chamada de LLM da triagem, sujeita ao mesmo orçamento por triagem
/// (<c>MaxLlmCallsPerTriagem</c>) e ao mesmo registro de uso/custo por
/// tenant (ADR-048).
/// </summary>
public interface ITriagemAttachmentVisionClient
{
    Task<TriagemAttachmentVisionResult> ClassificarAsync(
        TriagemAttachmentVisionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Entrada da classificação: os bytes do anexo (lidos do armazenamento
/// durável — nunca persistidos fora dele) e o tipo de conteúdo declarado.
/// </summary>
public sealed record TriagemAttachmentVisionRequest(
    Guid TenantId,
    Guid TriagemId,
    Guid MediaId,
    byte[] ContentBytes,
    string ContentType);

/// <summary>
/// Saída estruturada da classificação: tipo detectado, legibilidade e,
/// quando aplicável, modelo/número de série da etiqueta ou
/// número/série/data de emissão da nota fiscal (incremento de garantia). A
/// regra determinística de qual item do checklist é marcado como atendido
/// vive no domínio/serviço que consome este resultado — o adaptador nunca
/// decide isso sozinho.
/// </summary>
public sealed record TriagemAttachmentVisionPayload(
    ETriagemAttachmentVisionTipo Tipo,
    bool Legivel,
    string? Modelo,
    string? NumeroSerie,
    double Confianca,
    string? NumeroNotaFiscal = null,
    string? SerieNotaFiscal = null,
    DateTimeOffset? DataEmissaoNotaFiscal = null);

/// <summary>
/// Resultado de uma chamada a <see cref="ITriagemAttachmentVisionClient"/>.
/// Nunca lança exceção para falhas esperadas do fornecedor — isso é
/// representado por <see cref="Outcome"/>. Falha do provedor nunca quebra o
/// fluxo da triagem: o item correspondente apenas fica pendente para nova
/// tentativa em um turno futuro.
/// </summary>
public sealed record TriagemAttachmentVisionResult(
    ETriagemLlmOutcome Outcome,
    TriagemAttachmentVisionPayload? Payload,
    TriagemLlmUsageInfo? Usage,
    string? ErrorReason)
{
    public static TriagemAttachmentVisionResult Success(TriagemAttachmentVisionPayload payload, TriagemLlmUsageInfo usage) =>
        new(ETriagemLlmOutcome.Success, payload, usage, ErrorReason: null);

    public static TriagemAttachmentVisionResult Invalid(string reason, TriagemLlmUsageInfo? usage = null) =>
        new(ETriagemLlmOutcome.InvalidOutput, Payload: null, usage, reason);

    public static TriagemAttachmentVisionResult Unavailable(string reason) =>
        new(ETriagemLlmOutcome.ProviderUnavailable, Payload: null, Usage: null, reason);
}
