namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Filtro de OS por situação crítica/risco (RF-033.4) — conceito fechado do ATUA,
/// derivado de quanto tempo uma OS aberta está sem atualização (<c>WorkOrder.UpdatedAt</c>).
/// </summary>
public enum EWorkOrderCriticalityFilter
{
    Critical,
    AtRisk,
}
