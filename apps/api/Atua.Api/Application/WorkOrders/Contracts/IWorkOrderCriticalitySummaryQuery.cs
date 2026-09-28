namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// RF-033: contagem de OS abertas em situação crítica ou de risco, com base em quanto
/// tempo estão sem atualização (<c>WorkOrder.UpdatedAt</c>) — alimenta os cards de
/// Situação Crítica e Risco do Dashboard.
/// </summary>
public interface IWorkOrderCriticalitySummaryQuery
{
    Task<WorkOrderCriticalitySummaryResult> ExecuteAsync(Guid tenantId, DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>Resultado da contagem de OS abertas em situação crítica/risco.</summary>
public sealed record WorkOrderCriticalitySummaryResult(int Critical, int AtRisk);
