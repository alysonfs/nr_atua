using Atua.Api.Application.LlmUsage;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Detalha uma triagem do pool (ou já assumida) para o painel de suporte do
/// operador (épico de pool de triagem, "C2 — Detalhe"). Isolamento por
/// tenant obrigatório; triagem assumida só é visível para quem assumiu ou
/// para gestores (OWNER/ADMIN) — critério de aceite 8.
/// </summary>
public interface IGetTriagemPoolDetailUseCase
{
    Task<TriagemPoolDetailResult> ExecuteAsync(Guid tenantId, Guid triagemId, Guid requestingUserId,
        CancellationToken cancellationToken);
}

public enum ETriagemPoolDetailOutcome
{
    Success,

    /// <summary>Usuário autenticado não é membro do tenant.</summary>
    Forbidden,

    /// <summary>
    /// Triagem inexistente neste tenant, fora do pool/não assumida ainda, ou
    /// assumida por outro operador que não é gestor nem o próprio requisitante
    /// — tratados uniformemente como 404 para não revelar a existência do
    /// recurso a quem não tem acesso (critérios de aceite 2 e 8).
    /// </summary>
    NotFound,
}

public sealed record TriagemPoolDetailResult(ETriagemPoolDetailOutcome Outcome, TriagemPoolDetailView? Detail)
{
    public static TriagemPoolDetailResult Failure(ETriagemPoolDetailOutcome outcome) => new(outcome, null);
}

/// <param name="StateReason">Motivo do estado atual (ex.: expirada em 48h, falha de contato, 3 desvios) — épico, "C2 — Detalhe".</param>
/// <param name="WorkOrderId">Id da OS vinculada — mesmo id usado pela rota do Office <c>/work-orders/:workOrderId</c> (<c>WorkOrder.Id</c>); idêntico a <see cref="TriagemPoolWorkOrderView.WorkOrderId"/>.</param>
/// <param name="StartedAtUtc">Momento em que a triagem foi criada (<see cref="Triagem.CreatedAtUtc"/>) — início do tempo de triagem no Office.</param>
/// <param name="FinishedAtUtc">Momento em que a coleta da LLM terminou (<see cref="Triagem.ColetaEncerradaEmUtc"/>); <c>null</c> enquanto ainda em coleta.</param>
/// <param name="WarrantyEstimate">
/// Indicador de garantia calculado na leitura a partir de
/// <see cref="Triagem.DataEmissaoNotaFiscal"/> (estimativa provisória e
/// global — decisão do PO; nunca persistido).
/// </param>
/// <param name="NotaFiscalNumero">Número da nota fiscal extraído pela classificação de visão (<see cref="Triagem.NumeroNotaFiscal"/>); <c>null</c> quando ainda não extraído.</param>
/// <param name="NotaFiscalSerie">Série da nota fiscal — mesma origem e ressalva de <paramref name="NotaFiscalNumero"/>.</param>
/// <param name="LlmUsage">Totais de chamadas/tokens/custo de LLM desta triagem (ADR-048) — inclui classificação de visão e conversa.</param>
public sealed record TriagemPoolDetailView(
    Guid TriagemId,
    ETriagemStatus Status,
    string StateReason,
    DateTimeOffset EnteredPoolAtUtc,
    Guid? AssumidaPorUserId,
    DateTimeOffset? AssumidaEmUtc,
    Guid WorkOrderId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    WarrantyEstimate WarrantyEstimate,
    string? NotaFiscalNumero,
    string? NotaFiscalSerie,
    TriagemLlmCostSummary LlmUsage,
    TriagemPoolWorkOrderView WorkOrder,
    IReadOnlyList<TriagemPoolChecklistItemView> Checklist,
    IReadOnlyList<TriagemPoolAttachmentView> Attachments,
    IReadOnlyList<TriagemPoolConversationEntryView> Conversation);

public sealed record TriagemPoolWorkOrderView(
    Guid WorkOrderId,
    string? WorkOrderProviderNo,
    string? CustomerName,
    string? CustomerCpf,
    string? ContactPhone,
    string? ContactEmail,
    string? Address,
    string? ZipCode,
    string? CityName,
    string? StateName,
    string? ProductBrand,
    string? ProductModel,
    string? Symptom);

/// <param name="Required">Obrigatório para o checklist mínimo (descrição, nota fiscal, etiqueta, mídia, endereço) ou opcional (ponto de referência, localização).</param>
public sealed record TriagemPoolChecklistItemView(
    ETriagemChecklistItem Item,
    bool Required,
    bool Completed,
    string? Value);

/// <param name="Url">Link temporário e autorizado para leitura do anexo (ver <see cref="ITriagemInboundMediaStorage.CreatePresignedReadUrlAsync"/>).</param>
/// <param name="UrlExpiresAtUtc">Momento em que <paramref name="Url"/> deixa de ser válida.</param>
public sealed record TriagemPoolAttachmentView(
    Guid MediaId,
    ETriagemInboundMediaKind MediaKind,
    string ContentType,
    bool IsVoiceNote,
    int? DurationSeconds,
    string? Caption,
    DateTimeOffset ReceivedAtUtc,
    Uri Url,
    DateTimeOffset UrlExpiresAtUtc);

/// <param name="Sender">"consumidor" ou "posto" (nunca o conteúdo é atribuído à LLM — autoria é sempre o canal/consumidor, épico seção 7).</param>
public sealed record TriagemPoolConversationEntryView(
    DateTimeOffset AtUtc,
    string Sender,
    string Kind,
    string? Text,
    Guid? AttachmentMediaId);
