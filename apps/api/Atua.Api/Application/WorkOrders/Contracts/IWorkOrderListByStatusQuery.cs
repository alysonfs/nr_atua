namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// Lista paginada de OS derivada de <c>WorkOrder</c> (RF-017/RF-027/RF-030/RF-033, ver
/// <c>docs/architecture/dashboard-work-order-queries.md</c>), filtrada por status exato OU
/// por tempo mínimo de abertura (<c>minAgeDays</c>) OU por prazo de próximo atendimento
/// (<c>nextAppointmentFilter</c>) OU por situação crítica/risco (<c>criticalityFilter</c>) —
/// os quatro são mutuamente exclusivos: exatamente um deve ser informado.
/// </summary>
public interface IWorkOrderListByStatusQuery
{
    Task<WorkOrderListResult> ExecuteAsync(
        Guid tenantId, string? status, int? minAgeDays,
        Domain.WorkOrders.EWorkOrderNextAppointmentFilter? nextAppointmentFilter,
        Domain.WorkOrders.EWorkOrderCriticalityFilter? criticalityFilter,
        string timeZoneId, int page, int pageSize, DateTimeOffset now,
        CancellationToken cancellationToken);
}


/// <summary>Resultado paginado da consulta de OS por status, aging, prazo ou criticidade.</summary>
public sealed record WorkOrderListResult(
    string? Status,
    int? MinAgeDays,
    Domain.WorkOrders.EWorkOrderNextAppointmentFilter? NextAppointmentFilter,
    Domain.WorkOrders.EWorkOrderCriticalityFilter? CriticalityFilter,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<WorkOrderListItemResult> Items);

/// <summary>Item de OS retornado pela listagem por status.</summary>
public sealed record WorkOrderListItemResult(
    Guid Id,
    string WorkOrderProviderId,
    string? WorkOrderProviderNo,
    string? ServiceRequestId,
    decimal? Amount,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderUpdatedAt,
    string? ProductModel,
    string? ProductBrand,
    string? CustomerName,
    string? CityName,
    string ProviderName);
