namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// Sumário mensal de OS por status, derivado de <c>WorkOrderHistory</c>
/// (RF-017, ver <c>docs/architecture/dashboard-work-order-queries.md</c>).
/// </summary>
public interface IWorkOrderMonthlySummaryQuery
{
    Task<WorkOrderMonthlySummaryResult> ExecuteAsync(
        Guid tenantId, DateOnly monthStart, string timeZoneId,
        CancellationToken cancellationToken);
}

/// <summary>Resultado da consulta de sumário mensal de OS por status.</summary>
public sealed record WorkOrderMonthlySummaryResult(
    string Month,
    string TimeZoneId,
    IReadOnlyList<WorkOrderStatusMonthSummaryResult> Statuses);

/// <summary>Contagem diária de um status observado no mês.</summary>
public sealed record WorkOrderStatusMonthSummaryResult(
    string Status,
    int Total,
    IReadOnlyList<int> DailyCounts);
