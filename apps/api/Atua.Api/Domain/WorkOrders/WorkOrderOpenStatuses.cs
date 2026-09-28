namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Lista explícita de status "Em Aberto" (RF-029/ADR-033) — strings exatas do
/// provedor (case-sensitive), não um conjunto fechado controlado pelo ATUA. Classe estática
/// (não enum) porque os valores são livres do provedor, não um domínio do ATUA. Duplica
/// intencionalmente <c>Atua.Collector.Consumer.WorkOrderOpenStatuses</c> — ver ADR-032/ADR-033
/// para o racional da duplicação e o critério de quando unificá-la.
/// </summary>
public static class WorkOrderOpenStatuses
{
    public static IReadOnlySet<string> Values { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "pending", "assigned", "accepted", "Exchange Proposal", "Request to Explain",
    };

    public static bool IsOpen(string status) => Values.Contains(status);
}
