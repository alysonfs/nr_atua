namespace Atua.Collector.Consumer;

/// <summary>
/// Status terminais confirmados de uma OS (RF-026.3/RN-026.3/ADR-031) — strings exatas do
/// provedor (case-sensitive), não um conjunto fechado controlado pelo ATUA. Classe estática
/// (não enum) porque os valores são livres do provedor, não um domínio do ATUA.
/// Centraliza a lista para não duplicá-la entre <see cref="WorkOrderPgRepository"/> e testes.
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
