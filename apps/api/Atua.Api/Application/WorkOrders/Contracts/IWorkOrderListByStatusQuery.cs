namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// Lista paginada de OS por status atual, derivada de <c>WorkOrder</c>
/// (RF-017, ver <c>docs/architecture/dashboard-work-order-queries.md</c>).
/// </summary>
public interface IWorkOrderListByStatusQuery
{
    Task<WorkOrderListResult> ExecuteAsync(
        Guid tenantId, string status, int page, int pageSize,
        CancellationToken cancellationToken);
}

/// <summary>Resultado paginado da consulta de OS por status.</summary>
public sealed record WorkOrderListResult(
    string Status,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<WorkOrderListItemResult> Items);

/// <summary>Item de OS retornado pela listagem por status.</summary>
public sealed record WorkOrderListItemResult(
    Guid Id,
    string ProviderId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderUpdatedAt,
    string? ProductModel,
    string? ProductBrand,
    string? CustomerName,
    string? CityName);
