namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// RF-030.3/RF-030.4: contagem de OS abertas por prazo de próximo atendimento
/// (<c>NextAppointmentAt</c>), no fuso do tenant — alimenta os 3 cards de prazo do
/// Dashboard (Vencido / Vence Hoje / Vence Amanhã).
/// </summary>
public interface IWorkOrderNextAppointmentSummaryQuery
{
    Task<WorkOrderNextAppointmentSummaryResult> ExecuteAsync(
        Guid tenantId, string timeZoneId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Resultado da contagem de OS abertas por prazo de próximo atendimento.</summary>
public sealed record WorkOrderNextAppointmentSummaryResult(int Overdue, int DueToday, int DueTomorrow);
