using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Contrato que não depende de fornecedor para a condução da conversa pela
/// LLM (`triagem-llm-conducao.md` §2, ADR-048). A Master API é a única
/// autoridade: a implementação apenas propõe uma resposta e campos
/// estruturados — nunca aplica transição de estado, nunca agenda, cancela
/// ou conclui uma OS, nunca classifica <c>SubStatus</c>.
///
/// Minimização (LGPD): a entrada nunca contém CPF, telefone, endereço
/// completo ou nome do consumidor, salvo quando indispensável — ver
/// <see cref="TriagemLlmRequest"/>.
/// </summary>
public interface ITriagemLlmClient
{
    Task<TriagemLlmResult> ConduzirAsync(TriagemLlmRequest request, CancellationToken cancellationToken);
}

/// <summary>Estado de um item do checklist mínimo, para informar à LLM o que já foi atendido e o que falta.</summary>
public sealed record TriagemLlmChecklistItemState(ETriagemChecklistItem Item, bool Atendido);

/// <summary>
/// Um turno do histórico de conversa, só em texto, dentro da janela
/// minimizada enviada à LLM. Mídias nunca entram aqui como binário — ver
/// <see cref="TriagemLlmAttachmentMetadata"/>.
/// </summary>
public sealed record TriagemLlmConversationTurn(ETriagemLlmConversationRole Role, string Texto);

/// <summary>
/// Metadados de um anexo recebido — nunca o binário. Usado para a LLM saber
/// que uma imagem/vídeo chegou e de que tipo é, sem enviar conteúdo.
/// </summary>
public sealed record TriagemLlmAttachmentMetadata(
    Guid MediaId,
    ETriagemInboundMediaKind Kind,
    ETriagemChecklistItem? ChecklistItemCandidato);

/// <summary>
/// Entrada mínima da LLM (`triagem-llm-conducao.md` §2): checklist
/// atendido/pendente, janela de histórico em texto, e só os dados da OS
/// estritamente necessários (produto e marca) — nunca CPF, telefone,
/// endereço completo ou nome do consumidor.
/// </summary>
public sealed record TriagemLlmRequest(
    Guid TenantId,
    Guid TriagemId,
    string TenantNomeComercial,
    string Produto,
    string? Marca,
    bool JaApresentouIdentidade,
    IReadOnlyList<TriagemLlmChecklistItemState> ChecklistState,
    IReadOnlyList<TriagemLlmConversationTurn> HistoricoConversa,
    IReadOnlyList<TriagemLlmAttachmentMetadata> AnexosRecebidos);

/// <summary>Um campo estruturado proposto pela LLM, com o grau de confiança declarado (`triagem-llm-conducao.md` §2).</summary>
public sealed record TriagemLlmCampoEstruturado(string Valor, double Confianca);

/// <summary>
/// Campos estruturados propostos (B4): cada um é opcional — a LLM só
/// preenche o que conseguiu identificar na conversa, nunca inventa.
/// </summary>
public sealed record TriagemLlmCamposEstruturados(
    TriagemLlmCampoEstruturado? TipoDefeito,
    TriagemLlmCampoEstruturado? Sintoma,
    TriagemLlmCampoEstruturado? CodigoErro,
    TriagemLlmCampoEstruturado? Categoria);

/// <summary>Item do checklist que a LLM considera atendido, com a evidência (mensagem ou anexo) que fundamenta a marcação.</summary>
public sealed record TriagemLlmItemAtendido(ETriagemChecklistItem Item, string Evidencia);

/// <summary>Indica que a conversa saiu do escopo da triagem ou que o consumidor pediu atendimento humano.</summary>
public sealed record TriagemLlmSinalDesvio(bool Detectado, string? Motivo);

/// <summary>Saída estruturada validada contra o esquema (`triagem-llm-conducao.md` §2).</summary>
public sealed record TriagemLlmSuccessPayload(
    string RespostaAoConsumidor,
    TriagemLlmCamposEstruturados CamposEstruturados,
    IReadOnlyList<TriagemLlmItemAtendido> ItensAtendidos,
    TriagemLlmSinalDesvio SinalDesvio);

/// <summary>Consumo de tokens, modelo e latência de uma chamada — base do registro de uso por tenant (ADR-048).</summary>
public sealed record TriagemLlmUsageInfo(string ModelId, int InputTokens, int OutputTokens, TimeSpan Latencia);

/// <summary>
/// Resultado de uma chamada a <see cref="ITriagemLlmClient"/>. Nunca lança
/// exceção para falhas esperadas do fornecedor (indisponibilidade, saída
/// fora do esquema) — isso é representado por <see cref="Outcome"/>.
/// <see cref="Usage"/> pode estar presente mesmo quando <see cref="Outcome"/>
/// é <see cref="ETriagemLlmOutcome.InvalidOutput"/> (o fornecedor consumiu
/// tokens antes de produzir uma saída inválida).
/// </summary>
public sealed record TriagemLlmResult(
    ETriagemLlmOutcome Outcome,
    TriagemLlmSuccessPayload? Payload,
    TriagemLlmUsageInfo? Usage,
    string? ErrorReason)
{
    public static TriagemLlmResult Success(TriagemLlmSuccessPayload payload, TriagemLlmUsageInfo usage) =>
        new(ETriagemLlmOutcome.Success, payload, usage, ErrorReason: null);

    public static TriagemLlmResult Invalid(string reason, TriagemLlmUsageInfo? usage = null) =>
        new(ETriagemLlmOutcome.InvalidOutput, Payload: null, usage, reason);

    public static TriagemLlmResult Unavailable(string reason) =>
        new(ETriagemLlmOutcome.ProviderUnavailable, Payload: null, Usage: null, reason);
}
