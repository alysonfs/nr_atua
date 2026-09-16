using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-017: lista paginada de OS no estado atual (<c>WorkOrder</c>) filtradas
/// por status, ordenadas por atualização mais recente primeiro.
/// </summary>
public sealed class WorkOrderListByStatusQueryHandler(AtuaDbContext dbContext) : IWorkOrderListByStatusQuery
{
    public async Task<WorkOrderListResult> ExecuteAsync(Guid tenantId, string status, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var baseQuery = dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.TenantId == tenantId && workOrder.Status == status);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var items = await baseQuery
            .OrderByDescending(workOrder => workOrder.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(workOrder => new WorkOrderListItemResult(workOrder.Id, workOrder.ProviderId,
                workOrder.Status, workOrder.CreatedAt, workOrder.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new WorkOrderListResult(status, page, pageSize, totalCount, items);
    }
}
