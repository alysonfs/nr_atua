namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// Tendência mensal de OS criadas vs. concluídas, derivada de
/// <c>WorkOrder.CreatedAt</c> (criação) e <c>WorkOrderHistory</c> (entrada
/// com status equivalente a "fechado"/"closed" — conclusão), RF-017/DP-017.2.
/// </summary>
public interface IWorkOrderMonthlyTrendQuery
{
    Task<WorkOrderMonthlyTrendResult> ExecuteAsync(
        Guid tenantId, int months, string timeZoneId, CancellationToken cancellationToken);
}

/// <summary>Resultado da consulta de tendência mensal (últimos N meses, ordem cronológica).</summary>
public sealed record WorkOrderMonthlyTrendResult(IReadOnlyList<WorkOrderTrendMonthResult> Months);

/// <summary>Contagem de OS criadas e concluídas em um mês (formato "yyyy-MM").</summary>
public sealed record WorkOrderTrendMonthResult(string Month, int Created, int Completed);
