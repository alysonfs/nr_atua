using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-033: comparação direta em SQL (sem materializar em memória) — <c>UpdatedAt</c> já é
/// <c>DateTimeOffset</c> UTC e o critério não depende do fuso do tenant, ao contrário de
/// <see cref="WorkOrderNextAppointmentSummaryQueryHandler"/>.
/// </summary>
public sealed class WorkOrderCriticalitySummaryQueryHandler(AtuaDbContext dbContext)
    : IWorkOrderCriticalitySummaryQuery
{
    private const int CriticalThresholdDays = 5;
    private const int AtRiskThresholdDays = 4;

    public async Task<WorkOrderCriticalitySummaryResult> ExecuteAsync(Guid tenantId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var openStatuses = WorkOrderOpenStatuses.Values;
        var openWorkOrders = dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.TenantId == tenantId && openStatuses.Contains(workOrder.Status));

        var critical = await openWorkOrders.CountAsync(
            workOrder => workOrder.UpdatedAt <= now.AddDays(-CriticalThresholdDays), cancellationToken);

        var atRisk = await openWorkOrders.CountAsync(
            workOrder => workOrder.UpdatedAt <= now.AddDays(-AtRiskThresholdDays)
                         && workOrder.UpdatedAt > now.AddDays(-CriticalThresholdDays),
            cancellationToken);

        return new WorkOrderCriticalitySummaryResult(critical, atRisk);
    }
}
