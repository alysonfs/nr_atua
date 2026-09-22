namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// Detalhe completo de uma OS para exibição no Office (RF-026.5/RF-026.6),
/// incluindo o histórico de transições de status.
/// </summary>
public interface IWorkOrderDetailQuery
{
    Task<WorkOrderDetailResult?> ExecuteAsync(Guid tenantId, Guid workOrderId, CancellationToken cancellationToken);
}

/// <summary>Resultado completo de uma OS, com os campos normalizados relevantes para exibição.</summary>
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
    IReadOnlyList<WorkOrderHistoryEntryResult> History);

/// <summary>Entrada de histórico de status de uma OS, ordenada da mais antiga para a mais recente.</summary>
public sealed record WorkOrderHistoryEntryResult(string Status, DateTimeOffset CreatedAt);
