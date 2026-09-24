using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-017: tendência mensal de OS criadas (<c>WorkOrder.CreatedAt</c>) vs.
/// concluídas (entradas de <c>WorkOrderHistory</c> com status equivalente a
/// "fechado"/"closed"), nos últimos N meses, no fuso horário do tenant.
/// </summary>
public sealed class WorkOrderMonthlyTrendQueryHandler(AtuaDbContext dbContext, TimeProvider timeProvider)
    : IWorkOrderMonthlyTrendQuery
{
    // DP-017.2: status é string crua do provedor, sem enum/catálogo. "closed" é o único
    // valor observado até o momento para conclusão (ver WorkOrderTerminalStatuses no
    // Collector, que não pode ser referenciado daqui por não haver projeto compartilhado).
    private const string CompletedStatus = "closed";

    public async Task<WorkOrderMonthlyTrendResult> ExecuteAsync(Guid tenantId, int months, string timeZoneId,
        CancellationToken cancellationToken)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone);
        var currentMonthStart = new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var rangeStartLocal = currentMonthStart.AddMonths(-(months - 1));
        var rangeEndLocalExclusive = currentMonthStart.AddMonths(1);

        var rangeStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, timeZone),
            TimeSpan.Zero);
        var rangeEndUtcExclusive = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(rangeEndLocalExclusive, timeZone), TimeSpan.Zero);

        var createdAtTimestamps = await dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.TenantId == tenantId
                                 && workOrder.CreatedAt >= rangeStartUtc
                                 && workOrder.CreatedAt < rangeEndUtcExclusive)
            .Select(workOrder => workOrder.CreatedAt)
            .ToListAsync(cancellationToken);

        var completedAtTimestamps = await dbContext.WorkOrderHistories.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId
                            && entry.CreatedAt >= rangeStartUtc
                            && entry.CreatedAt < rangeEndUtcExclusive
                            && entry.Status.ToLower() == CompletedStatus)
            .Select(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken);

        var createdByMonth = CountByLocalMonth(createdAtTimestamps, timeZone);
        var completedByMonth = CountByLocalMonth(completedAtTimestamps, timeZone);

        var result = new List<WorkOrderTrendMonthResult>(months);
        for (var i = 0; i < months; i++)
        {
            var monthStart = rangeStartLocal.AddMonths(i);
            var key = (monthStart.Year, monthStart.Month);
            createdByMonth.TryGetValue(key, out var created);
            completedByMonth.TryGetValue(key, out var completed);
            result.Add(new WorkOrderTrendMonthResult($"{monthStart.Year:D4}-{monthStart.Month:D2}", created,
                completed));
        }

        return new WorkOrderMonthlyTrendResult(result);
    }

    private static Dictionary<(int Year, int Month), int> CountByLocalMonth(
        IReadOnlyList<DateTimeOffset> timestampsUtc, TimeZoneInfo timeZone)
    {
        var counts = new Dictionary<(int Year, int Month), int>();
        foreach (var timestampUtc in timestampsUtc)
        {
            var local = TimeZoneInfo.ConvertTime(timestampUtc, timeZone);
            var key = (local.Year, local.Month);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }
}
