namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Status de OS considerado elegível para iniciar a triagem automatizada
/// (épico de triagem WhatsApp Fase 1, seção 1: "O Posto Autorizado recebe OS
/// 'assigned' do provedor"; arquitetura, seção 2.1). Comparação deliberada e
/// exata (case-sensitive) com a string crua do provedor — mesmo padrão de
/// <see cref="Atua.Api.Domain.WorkOrders.WorkOrderOpenStatuses"/> e
/// <see cref="Atua.Api.Domain.WorkOrders.WorkOrderTerminalStatuses"/>.
/// Classe estática (não enum) porque o valor é livre do provedor, não um
/// domínio fechado controlado pelo ATUA.
///
/// Esta fase define um único status elegível. Diferente de
/// <see cref="Atua.Api.Domain.WorkOrders.WorkOrderOpenStatuses"/> (que cobre
/// vários status "em aberto" para outra finalidade), a elegibilidade de
/// início da triagem é propositalmente mais restrita.
/// </summary>
public static class TriagemEligibleWorkOrderStatus
{
    /// <summary>Único status elegível para iniciar a triagem nesta fase.</summary>
    public const string Assigned = "assigned";

    /// <summary>
    /// Compara deliberadamente de forma exata e ordinal (case-sensitive) com
    /// <see cref="Assigned"/> — nunca normaliza, nem aceita variações de
    /// caixa do provedor.
    /// </summary>
    public static bool IsEligible(string status) => string.Equals(status, Assigned, StringComparison.Ordinal);
}
