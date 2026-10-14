namespace Atua.Collector.Consumer;

/// <summary>
/// Status de OS que dispara a gravação de uma intenção de início de triagem
/// em <c>triagem_trigger_outbox</c> (ADR-041). Comparação deliberada e exata
/// (case-sensitive, <see cref="StringComparison.Ordinal"/>) com a string
/// crua do provedor — mesmo padrão de <see cref="WorkOrderOpenStatuses"/> e
/// <see cref="WorkOrderTerminalStatuses"/>. Classe estática (não enum)
/// porque o valor é livre do provedor, não um domínio fechado controlado
/// pelo ATUA.
///
/// Duplica intencionalmente
/// <c>Atua.Api.Domain.Triagens.TriagemEligibleWorkOrderStatus</c> — ver
/// ADR-032/ADR-033 para o racional da duplicação entre Collector e Master
/// API e o critério de quando unificá-la.
/// </summary>
public static class TriagemTriggerEligibility
{
    /// <summary>Único status elegível para gravar a intenção de triagem nesta fase.</summary>
    public const string Assigned = "assigned";

    /// <summary>
    /// Compara deliberadamente de forma exata e ordinal (case-sensitive) com
    /// <see cref="Assigned"/> — nunca normaliza, nem aceita variações de
    /// caixa do provedor.
    /// </summary>
    public static bool IsEligible(string status) => string.Equals(status, Assigned, StringComparison.Ordinal);
}
