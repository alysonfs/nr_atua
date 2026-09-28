namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Responsável pela resolução de uma pendência de OS (RF-031) — conceito fechado e
/// controlado pelo ATUA (diferente de <c>WorkOrder.SubStatus</c>, que é texto livre do
/// provedor), por isso modelado como enum.
/// </summary>
public enum EWorkOrderSubStatusResponsible
{
    PostoAutorizado,
    MideaCallcenter,
    MideaPlanejamento,
    MideaCorreios,
    MideaJira,
    MideaJuridico,
    MideaSuporteTecnico,
    NaoMapeado,
}
