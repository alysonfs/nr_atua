namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// RF-027: contagem de OS abertas (status fora de
/// <c>WorkOrderTerminalStatuses</c>) por faixa de tempo de abertura,
/// cumulativa e não mutuamente exclusiva entre as 3 faixas (+5/+10/+30
/// dias) — alimenta os cards de aging do Dashboard.
/// </summary>
public interface IWorkOrderAgingSummaryQuery
{
    Task<WorkOrderAgingSummaryResult> ExecuteAsync(Guid tenantId, DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>Resultado da contagem de OS abertas por faixa de aging.</summary>
public sealed record WorkOrderAgingSummaryResult(int MoreThan5Days, int MoreThan10Days, int MoreThan30Days);
