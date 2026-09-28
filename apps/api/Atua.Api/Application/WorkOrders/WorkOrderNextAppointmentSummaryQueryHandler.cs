using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-030.3/RF-030.4: cálculo em memória (não SQL) porque o critério depende do fuso do
/// tenant e de "hoje" no momento da execução — mesmo precedente de
/// <see cref="WorkOrderMonthlyTrendQueryHandler"/>.
/// </summary>
public sealed class WorkOrderNextAppointmentSummaryQueryHandler(AtuaDbContext dbContext)
    : IWorkOrderNextAppointmentSummaryQuery
{
    public async Task<WorkOrderNextAppointmentSummaryResult> ExecuteAsync(Guid tenantId, string timeZoneId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var openStatuses = WorkOrderOpenStatuses.Values;
        var nextAppointments = await dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.TenantId == tenantId
                                 && openStatuses.Contains(workOrder.Status)
                                 && workOrder.NextAppointmentAt != null)
            .Select(workOrder => workOrder.NextAppointmentAt!.Value)
            .ToListAsync(cancellationToken);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var tomorrow = today.AddDays(1);

        var overdue = 0;
        var dueToday = 0;
        var dueTomorrow = 0;

        foreach (var nextAppointmentAt in nextAppointments)
        {
            var appointmentDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nextAppointmentAt, timeZone).DateTime);

            if (appointmentDate < today) overdue++;
            else if (appointmentDate == today) dueToday++;
            else if (appointmentDate == tomorrow) dueTomorrow++;
        }

        return new WorkOrderNextAppointmentSummaryResult(overdue, dueToday, dueTomorrow);
    }
}
