namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Status terminais confirmados de uma OS (RF-027) — strings exatas do
/// provedor (case-sensitive), não um conjunto fechado controlado pelo ATUA.
/// Classe estática (não enum) porque os valores são livres do provedor, não
/// um domínio do ATUA. Duplica intencionalmente
/// <c>Atua.Collector.Consumer.WorkOrderTerminalStatuses</c> — ver ADR-032
/// para o racional da duplicação e o critério de quando unificá-la.
/// </summary>
public static class WorkOrderTerminalStatuses
{
    public static IReadOnlySet<string> Values { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Payment Approved",
        "cancelled",
        "closed",
    };

    public static bool IsTerminal(string status) => Values.Contains(status);
}
