namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Filtro de OS por prazo do próximo atendimento (RF-030.5) — conceito fechado do ATUA,
/// derivado em memória a partir de <c>WorkOrder.NextAppointmentAt</c> e do fuso do tenant.
/// </summary>
public enum EWorkOrderNextAppointmentFilter
{
    Overdue,
    DueToday,
    DueTomorrow,
}
