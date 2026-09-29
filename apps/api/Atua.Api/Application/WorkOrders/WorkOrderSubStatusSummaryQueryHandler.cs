using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-032: agrupa OS abertas por <c>SubStatus</c> (SQL <c>GROUP BY</c>, tratando
/// <c>NULL</c> como uma chave própria), resolvendo o responsável em memória sobre os
/// grupos já agregados (poucos grupos distintos, custo desprezível).
/// </summary>
public sealed class WorkOrderSubStatusSummaryQueryHandler(AtuaDbContext dbContext) : IWorkOrderSubStatusSummaryQuery
{
    private const string NoSubStatusLabel = "Sem substatus";

    public async Task<WorkOrderSubStatusSummaryResult> ExecuteAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        var openStatuses = WorkOrderOpenStatuses.Values;

        var rawGroups = await dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.TenantId == tenantId && openStatuses.Contains(workOrder.Status))
            .GroupBy(workOrder => workOrder.SubStatus)
            .Select(group => new { SubStatus = group.Key, Total = group.Count() })
            .ToListAsync(cancellationToken);

        // Reagrupa em memória por categoria normalizada (ver
        // WorkOrderSubStatusCatalog.NormalizeCategory): o fallback em inglês
        // (closeDescription) traz texto livre único por OS junto da categoria, que o SQL
        // GroupBy acima ainda não separou.
        var result = rawGroups
            .GroupBy(group => group.SubStatus is null
                ? NoSubStatusLabel
                : WorkOrderSubStatusCatalog.NormalizeCategory(group.SubStatus))
            .Select(categoryGroup => new WorkOrderSubStatusCount(
                categoryGroup.Key,
                WorkOrderSubStatusCatalog.Resolve(categoryGroup.Key),
                categoryGroup.Sum(group => group.Total)))
            .ToList();

        return new WorkOrderSubStatusSummaryResult(result);
    }
}
