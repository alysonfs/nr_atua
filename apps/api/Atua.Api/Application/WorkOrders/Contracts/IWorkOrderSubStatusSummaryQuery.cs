using Atua.Api.Domain.WorkOrders;

namespace Atua.Api.Application.WorkOrders.Contracts;

/// <summary>
/// RF-032: contagem de OS abertas por substatus (<c>SubStatus</c>), com o responsável
/// resolvido via <see cref="WorkOrderSubStatusCatalog"/> — alimenta o card de indicador
/// por responsável do Dashboard.
/// </summary>
public interface IWorkOrderSubStatusSummaryQuery
{
    Task<WorkOrderSubStatusSummaryResult> ExecuteAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>Resultado da contagem de OS abertas agrupadas por substatus.</summary>
public sealed record WorkOrderSubStatusSummaryResult(IReadOnlyList<WorkOrderSubStatusCount> Groups);

/// <summary>Contagem de OS de um substatus, com o responsável resolvido pelo catálogo.</summary>
public sealed record WorkOrderSubStatusCount(
    string SubStatus,
    EWorkOrderSubStatusResponsible Responsible,
    int Total);
