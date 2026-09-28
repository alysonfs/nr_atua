using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-017/RF-027/RF-030/RF-033: lista paginada de OS no estado atual (<c>WorkOrder</c>),
/// filtradas por status exato OU por tempo mínimo de abertura (<c>minAgeDays</c>) OU por
/// prazo de próximo atendimento (<c>nextAppointmentFilter</c>) OU por situação
/// crítica/risco (<c>criticalityFilter</c>) — mutuamente exclusivos —, ordenadas por
/// atualização mais recente primeiro.
/// </summary>
public sealed class WorkOrderListByStatusQueryHandler(AtuaDbContext dbContext) : IWorkOrderListByStatusQuery
{
    private const int CriticalThresholdDays = 5;
    private const int AtRiskThresholdDays = 4;

    public async Task<WorkOrderListResult> ExecuteAsync(Guid tenantId, string? status, int? minAgeDays,
        EWorkOrderNextAppointmentFilter? nextAppointmentFilter, EWorkOrderCriticalityFilter? criticalityFilter,
        string timeZoneId, int page, int pageSize, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var baseQuery = dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.TenantId == tenantId);

        if (status is not null)
        {
            baseQuery = baseQuery.Where(workOrder => workOrder.Status == status);
        }
        else if (minAgeDays is not null)
        {
            baseQuery = ApplyAgingFilter(baseQuery, minAgeDays.Value, now);
        }
        else if (criticalityFilter is not null)
        {
            baseQuery = ApplyCriticalityFilter(baseQuery, criticalityFilter.Value, now);
        }

        // nextAppointmentFilter depende do fuso do tenant — não é traduzível para SQL pelo
        // EF Core (mesmo precedente de WorkOrderMonthlyTrendQueryHandler), então é aplicado
        // em memória sobre a página já reduzida por status/aging/criticidade + TenantId.
        List<Domain.WorkOrders.WorkOrder> filteredItems;
        int totalCount;
        if (nextAppointmentFilter is not null)
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var openStatuses = WorkOrderOpenStatuses.Values;
            var candidates = await baseQuery
                .Where(workOrder => openStatuses.Contains(workOrder.Status) && workOrder.NextAppointmentAt != null)
                .ToListAsync(cancellationToken);

            var matched = candidates
                .Where(workOrder => MatchesNextAppointmentFilter(workOrder.NextAppointmentAt!.Value, timeZone, now,
                    nextAppointmentFilter.Value))
                .OrderByDescending(workOrder => workOrder.UpdatedAt)
                .ToList();

            totalCount = matched.Count;
            filteredItems = matched.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        else
        {
            totalCount = await baseQuery.CountAsync(cancellationToken);
            filteredItems = await baseQuery
                .OrderByDescending(workOrder => workOrder.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

        var workOrderIds = filteredItems.Select(workOrder => workOrder.Id).ToList();
        var items = await dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrderIds.Contains(workOrder.Id))
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

        // Reordena pelo resultado já ordenado (o Join acima não preserva a ordem).
        var itemsById = items.ToDictionary(item => item.Id);
        var orderedItems = workOrderIds.Select(id => itemsById[id]).ToList();

        return new WorkOrderListResult(status, minAgeDays, nextAppointmentFilter, criticalityFilter, page, pageSize,
            totalCount, orderedItems);
    }

    private static IQueryable<Domain.WorkOrders.WorkOrder> ApplyAgingFilter(
        IQueryable<Domain.WorkOrders.WorkOrder> query, int minAgeDays, DateTimeOffset now)
    {
        var threshold = now.AddDays(-minAgeDays);
        var openStatuses = WorkOrderOpenStatuses.Values;

        return query.Where(workOrder =>
            openStatuses.Contains(workOrder.Status)
            && (workOrder.ProviderCreatedAt ?? workOrder.CreatedAt) <= threshold);
    }

    private static IQueryable<Domain.WorkOrders.WorkOrder> ApplyCriticalityFilter(
        IQueryable<Domain.WorkOrders.WorkOrder> query, EWorkOrderCriticalityFilter criticalityFilter,
        DateTimeOffset now)
    {
        var openStatuses = WorkOrderOpenStatuses.Values;
        var criticalThreshold = now.AddDays(-CriticalThresholdDays);
        var atRiskThreshold = now.AddDays(-AtRiskThresholdDays);

        var openQuery = query.Where(workOrder => openStatuses.Contains(workOrder.Status));

        return criticalityFilter == EWorkOrderCriticalityFilter.Critical
            ? openQuery.Where(workOrder => workOrder.UpdatedAt <= criticalThreshold)
            : openQuery.Where(workOrder => workOrder.UpdatedAt <= atRiskThreshold && workOrder.UpdatedAt > criticalThreshold);
    }

    private static bool MatchesNextAppointmentFilter(DateTimeOffset nextAppointmentAt, TimeZoneInfo timeZone,
        DateTimeOffset now, EWorkOrderNextAppointmentFilter filter)
    {
        var appointmentDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nextAppointmentAt, timeZone).DateTime);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);

        return filter switch
        {
            EWorkOrderNextAppointmentFilter.Overdue => appointmentDate < today,
            EWorkOrderNextAppointmentFilter.DueToday => appointmentDate == today,
            EWorkOrderNextAppointmentFilter.DueTomorrow => appointmentDate == today.AddDays(1),
            _ => false,
        };
    }
}
