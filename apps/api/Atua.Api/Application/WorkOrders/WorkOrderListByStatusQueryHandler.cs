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
            .Join(dbContext.Integrations, workOrder => workOrder.IntegrationId, integration => integration.Id,
                (workOrder, integration) => new { workOrder, integration })
            .Join(dbContext.IntegrationProviders, wi => wi.integration.ProviderId, provider => provider.Id,
                (wi, provider) => new WorkOrderListItemResult(wi.workOrder.Id, wi.workOrder.WorkOrderProviderId,
                    wi.workOrder.WorkOrderProviderNo, wi.workOrder.ServiceRequestId, wi.workOrder.Amount,
                    wi.workOrder.Status, wi.workOrder.CreatedAt, wi.workOrder.UpdatedAt,
                    wi.workOrder.ProviderCreatedAt, wi.workOrder.ProviderUpdatedAt,
                    wi.workOrder.ProductModel, wi.workOrder.ProductBrand, wi.workOrder.CustomerName,
                    wi.workOrder.CityName, provider.Name))
            .ToListAsync(cancellationToken);

        return new WorkOrderListResult(status, page, pageSize, totalCount, items);
    }
}
