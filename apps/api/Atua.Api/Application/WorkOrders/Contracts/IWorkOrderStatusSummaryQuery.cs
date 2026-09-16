namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// Contagem de OS por status ATUAL (tabela <c>WorkOrder</c>), sem recorte de
/// mês/série diária (RF-017, ADR-027; ver
/// <c>docs/architecture/dashboard-work-order-queries.md</c>).
/// </summary>
public interface IWorkOrderStatusSummaryQuery
{
    Task<WorkOrderStatusSummaryResult> ExecuteAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>Resultado da consulta de contagem por status atual.</summary>
public sealed record WorkOrderStatusSummaryResult(IReadOnlyList<WorkOrderStatusCountResult> Statuses);

/// <summary>Contagem total de OS em um status, no estado atual.</summary>
public sealed record WorkOrderStatusCountResult(string Status, int Total);
