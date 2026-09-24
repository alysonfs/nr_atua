using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-026.5/RF-026.6: detalhe completo de uma OS, com histórico de status,
/// para a página de detalhes de OS no Office.
/// </summary>
public sealed class WorkOrderDetailQueryHandler(AtuaDbContext dbContext) : IWorkOrderDetailQuery
{
    public async Task<WorkOrderDetailResult?> ExecuteAsync(Guid tenantId, Guid workOrderId,
        CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.WorkOrders.AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == workOrderId && order.TenantId == tenantId,
                cancellationToken);
        if (workOrder is null) return null;

        var history = await dbContext.WorkOrderHistories.AsNoTracking()
            .Where(entry => entry.WorkOrderId == workOrderId && entry.TenantId == tenantId)
            .OrderBy(entry => entry.CreatedAt)
            .Select(entry => new WorkOrderHistoryEntryResult(entry.Status, entry.CreatedAt))
            .ToListAsync(cancellationToken);

        return new WorkOrderDetailResult(
            workOrder.Id,
            workOrder.WorkOrderProviderId,
            workOrder.WorkOrderProviderNo,
            workOrder.Status,
            workOrder.CreatedAt,
            workOrder.UpdatedAt,
            workOrder.ProviderCreatedAt,
            workOrder.ProviderUpdatedAt,
            workOrder.ServiceRequestId,
            workOrder.Amount,
            workOrder.CustomerType,
            workOrder.CustomerName,
            workOrder.CustomerCpf,
            workOrder.ContactEmail,
            workOrder.ContactPhone,
            workOrder.ContactName,
            workOrder.Address,
            workOrder.ZipCode,
            workOrder.CountryName,
            workOrder.StateName,
            workOrder.CityName,
            workOrder.ProductBrand,
            workOrder.PdCode,
            workOrder.CategoryId,
            workOrder.ProductCategoryCode,
            workOrder.ProductCode,
            workOrder.ProductModel,
            workOrder.ProductStatus,
            workOrder.Symptom,
            history);
    }
}
