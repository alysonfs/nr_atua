using Atua.Api.Application.LlmUsage;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// Detalhe completo de uma OS para exibição no Office (RF-026.5/RF-026.6),
/// incluindo o histórico de transições de status.
/// </summary>
public interface IWorkOrderDetailQuery
{
    Task<WorkOrderDetailResult?> ExecuteAsync(Guid tenantId, Guid workOrderId, CancellationToken cancellationToken);
}

/// <summary>
/// Resultado completo de uma OS, com os campos normalizados relevantes para
/// exibição. <see cref="TriagemId"/> referencia a triagem mais recente
/// daquela OS (mesmo tenant), usada pela rota /triage-pool/:triagemId no
/// Office; é <see langword="null"/> quando a OS nunca gerou triagem.
/// </summary>
/// <param name="TriagemStartedAtUtc">Momento em que a triagem foi criada (<see cref="Triagem.CreatedAtUtc"/>); <c>null</c> quando a OS nunca gerou triagem.</param>
/// <param name="TriagemFinishedAtUtc">Momento em que a coleta da LLM terminou (<see cref="Triagem.ColetaEncerradaEmUtc"/>); <c>null</c> enquanto ainda em coleta ou sem triagem.</param>
/// <param name="WarrantyEstimate">
/// Indicador de garantia calculado na leitura a partir de
/// <see cref="Triagem.DataEmissaoNotaFiscal"/> (estimativa provisória e
/// global — decisão do PO; nunca persistido). <see cref="EWarrantyEstimateStatus.Indeterminado"/> quando a OS nunca gerou triagem ou a nota fiscal ainda não foi identificada.
/// </param>
/// <param name="NotaFiscalNumero">Número da nota fiscal extraído pela classificação de visão (<see cref="Triagem.NumeroNotaFiscal"/>); <c>null</c> quando ainda não extraído ou sem triagem.</param>
/// <param name="NotaFiscalSerie">Série da nota fiscal — mesma origem e ressalva de <paramref name="NotaFiscalNumero"/>.</param>
/// <param name="LlmUsage">Totais de chamadas/tokens/custo de LLM da triagem desta OS (ADR-048); zerado quando a OS nunca gerou triagem.</param>
public sealed record WorkOrderDetailResult(
    Guid Id,
    string WorkOrderProviderId,
    string? WorkOrderProviderNo,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderUpdatedAt,
    string? ServiceRequestId,
    decimal? Amount,
    string? CustomerType,
    string? CustomerName,
    string? CustomerCpf,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactName,
    string? Address,
    string? ZipCode,
    string? CountryName,
    string? StateName,
    string? CityName,
    string? ProductBrand,
    string? PdCode,
    string? CategoryId,
    string? ProductCategoryCode,
    string? ProductCode,
    string? ProductModel,
    string? ProductStatus,
    string? Symptom,
    string? SymptomRaw,
    IReadOnlyList<WorkOrderHistoryEntryResult> History,
    Guid? TriagemId,
    DateTimeOffset? TriagemStartedAtUtc,
    DateTimeOffset? TriagemFinishedAtUtc,
    WarrantyEstimate WarrantyEstimate,
    string? NotaFiscalNumero,
    string? NotaFiscalSerie,
    TriagemLlmCostSummary LlmUsage);

/// <summary>Entrada de histórico de status de uma OS, ordenada da mais antiga para a mais recente.</summary>
public sealed record WorkOrderHistoryEntryResult(string Status, DateTimeOffset CreatedAt);
