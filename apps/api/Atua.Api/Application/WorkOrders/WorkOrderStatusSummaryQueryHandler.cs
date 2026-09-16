using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-017/ADR-027: contagem de OS por status atual, direto de
/// <c>WorkOrder</c> (sem reconstrução de histórico nem recorte de mês).
/// </summary>
public sealed class WorkOrderStatusSummaryQueryHandler(AtuaDbContext dbContext) : IWorkOrderStatusSummaryQuery
{
    public async Task<WorkOrderStatusSummaryResult> ExecuteAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.TenantId == tenantId)
            .GroupBy(workOrder => workOrder.Status)
            .Select(group => new { Status = group.Key, Total = group.Count() })
            .ToListAsync(cancellationToken);

        var statuses = counts
            .Select(count => new WorkOrderStatusCountResult(count.Status, count.Total))
            .OrderByDescending(status => status.Total)
            .ToList();

        return new WorkOrderStatusSummaryResult(statuses);
    }
}
