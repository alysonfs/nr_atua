using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-027/RF-029/ADR-033: conta OS abertas (status na whitelist explícita
/// <see cref="WorkOrderOpenStatuses"/>) cuja data de abertura
/// (<c>ProviderCreatedAt</c>, com fallback <c>CreatedAt</c>) já ultrapassou
/// cada uma das 3 faixas de aging. Faixas são cumulativas: uma OS aberta há
/// 40 dias conta nas 3 contagens.
/// </summary>
public sealed class WorkOrderAgingSummaryQueryHandler(AtuaDbContext dbContext) : IWorkOrderAgingSummaryQuery
{
    private const int FirstThresholdDays = 5;
    private const int SecondThresholdDays = 10;
    private const int ThirdThresholdDays = 30;

    public async Task<WorkOrderAgingSummaryResult> ExecuteAsync(Guid tenantId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var openStatuses = WorkOrderOpenStatuses.Values;
        var openWorkOrders = dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.TenantId == tenantId && openStatuses.Contains(workOrder.Status));


        var moreThan5Days = await CountOlderThanAsync(openWorkOrders, now, FirstThresholdDays, cancellationToken);
        var moreThan10Days = await CountOlderThanAsync(openWorkOrders, now, SecondThresholdDays, cancellationToken);
        var moreThan30Days = await CountOlderThanAsync(openWorkOrders, now, ThirdThresholdDays, cancellationToken);

        return new WorkOrderAgingSummaryResult(moreThan5Days, moreThan10Days, moreThan30Days);
    }

    private static Task<int> CountOlderThanAsync(IQueryable<Domain.WorkOrders.WorkOrder> openWorkOrders,
        DateTimeOffset now, int days, CancellationToken cancellationToken)
    {
        var threshold = now.AddDays(-days);
        return openWorkOrders.CountAsync(
            workOrder => (workOrder.ProviderCreatedAt ?? workOrder.CreatedAt) <= threshold, cancellationToken);
    }
}
