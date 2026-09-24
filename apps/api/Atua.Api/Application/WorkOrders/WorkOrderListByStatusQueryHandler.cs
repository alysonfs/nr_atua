using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-017/RF-027: lista paginada de OS no estado atual (<c>WorkOrder</c>),
/// filtradas por status exato OU por tempo mínimo de abertura
/// (<c>minAgeDays</c>, mutuamente exclusivos), ordenadas por atualização
/// mais recente primeiro.
/// </summary>
public sealed class WorkOrderListByStatusQueryHandler(AtuaDbContext dbContext) : IWorkOrderListByStatusQuery
{
    public async Task<WorkOrderListResult> ExecuteAsync(Guid tenantId, string? status, int? minAgeDays, int page,
        int pageSize, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var baseQuery = dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.TenantId == tenantId);

        baseQuery = status is not null
            ? baseQuery.Where(workOrder => workOrder.Status == status)
            : ApplyAgingFilter(baseQuery, minAgeDays!.Value, now);

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

        return new WorkOrderListResult(status, minAgeDays, page, pageSize, totalCount, items);
    }

    private static IQueryable<Domain.WorkOrders.WorkOrder> ApplyAgingFilter(
        IQueryable<Domain.WorkOrders.WorkOrder> query, int minAgeDays, DateTimeOffset now)
    {
        var threshold = now.AddDays(-minAgeDays);
        var terminalStatuses = WorkOrderTerminalStatuses.Values;

        return query.Where(workOrder =>
            !terminalStatuses.Contains(workOrder.Status)
            && (workOrder.ProviderCreatedAt ?? workOrder.CreatedAt) <= threshold);
    }
}
