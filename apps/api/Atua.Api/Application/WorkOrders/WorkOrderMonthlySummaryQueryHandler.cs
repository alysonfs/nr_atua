using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-017: reconstrói, para cada dia do mês, o status vigente de cada OS a
/// partir de <c>WorkOrderHistory</c> (entrada mais recente com
/// <c>CreatedAt</c> anterior ao início do dia seguinte, por
/// <c>WorkOrderId</c>), e agrega a contagem diária por status. Ver
/// <c>docs/architecture/dashboard-work-order-queries.md</c> para a regra de
/// derivação completa.
/// </summary>
public sealed class WorkOrderMonthlySummaryQueryHandler(AtuaDbContext dbContext)
    : IWorkOrderMonthlySummaryQuery
{
    public async Task<WorkOrderMonthlySummaryResult> ExecuteAsync(Guid tenantId, DateOnly monthStart,
        string timeZoneId, CancellationToken cancellationToken)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);

        // Limite superior (exclusivo) de cada dia do mês, em UTC: início do
        // dia seguinte no fuso horário do tenant.
        var dayUpperBoundsUtc = new DateTimeOffset[daysInMonth];
        for (var day = 1; day <= daysInMonth; day++)
        {
            var nextDayLocal = new DateTime(monthStart.Year, monthStart.Month, day, 0, 0, 0,
                DateTimeKind.Unspecified).AddDays(1);
            var nextDayUtc = TimeZoneInfo.ConvertTimeToUtc(nextDayLocal, timeZone);
            dayUpperBoundsUtc[day - 1] = new DateTimeOffset(nextDayUtc, TimeSpan.Zero);
        }

        var lastBound = dayUpperBoundsUtc[^1];

        var history = await dbContext.WorkOrderHistories.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.CreatedAt < lastBound)
            .OrderBy(entry => entry.WorkOrderId)
            .ThenBy(entry => entry.CreatedAt)
            .Select(entry => new { entry.WorkOrderId, entry.Status, entry.CreatedAt })
            .ToListAsync(cancellationToken);

        var dailyCountsByStatus = new Dictionary<string, int[]>();

        var cursor = 0;
        while (cursor < history.Count)
        {
            var workOrderId = history[cursor].WorkOrderId;
            var groupStart = cursor;
            while (cursor < history.Count && history[cursor].WorkOrderId == workOrderId) cursor++;

            var entryCursor = groupStart;
            for (var day = 0; day < daysInMonth; day++)
            {
                var upperBound = dayUpperBoundsUtc[day];
                while (entryCursor < cursor && history[entryCursor].CreatedAt < upperBound) entryCursor++;

                // Ainda não havia nenhum registro de histórico até o fim
                // deste dia: a OS ainda não existia/ não fora observada.
                if (entryCursor == groupStart) continue;

                var status = history[entryCursor - 1].Status;
                if (!dailyCountsByStatus.TryGetValue(status, out var counts))
                {
                    counts = new int[daysInMonth];
                    dailyCountsByStatus[status] = counts;
                }

                counts[day]++;
            }
        }

        var statuses = dailyCountsByStatus
            .Select(pair => new WorkOrderStatusMonthSummaryResult(pair.Key, pair.Value.Sum(), pair.Value))
            .OrderByDescending(summary => summary.Total)
            .ToList();

        var month = $"{monthStart.Year:D4}-{monthStart.Month:D2}";
        return new WorkOrderMonthlySummaryResult(month, timeZoneId, statuses);
    }
}
