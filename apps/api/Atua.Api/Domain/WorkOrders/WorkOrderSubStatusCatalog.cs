namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Catálogo de mapeamento substatus (<c>pendingReason</c> do provedor) → responsável
/// (RF-031.2). Dicionário estático em código, fonte única de verdade — apenas na API
/// (o Collector não precisa resolver o responsável, só persistir o valor bruto).
/// </summary>
public static class WorkOrderSubStatusCatalog
{
    private static readonly IReadOnlyDictionary<string, EWorkOrderSubStatusResponsible> Responsibles =
        new Dictionary<string, EWorkOrderSubStatusResponsible>(StringComparer.Ordinal)
        {
            ["Acordo de ressarcimento - pendente de coleta"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Acordo de ressarcimento - processo em andamento"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Acordo de troca - pendente de execução de troca"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Acordo de troca - pendente de remessa do novo produto"] = EWorkOrderSubStatusResponsible.MideaPlanejamento,
            ["Acordo de troca - processo em andamento"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Acordo de troca - produto em trânsito"] = EWorkOrderSubStatusResponsible.MideaPlanejamento,
            ["Call Center - aguardando resposta de Aprovação Especial"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Call Center - Aprovação Especial respondida"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Call Center - negociação de acordo em andamento"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Consumidor - aguardando confirmação de agendamento"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Consumidor - contato em andamento"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["E-Ticket - aguardando entrega do produto pelo Correios"] = EWorkOrderSubStatusResponsible.MideaCorreios,
            ["Jira - aguardando resposta de suporte"] = EWorkOrderSubStatusResponsible.MideaJira,
            ["Jira - suporte concluído aguardando atuação do Posto Autorizado"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Jurídico - aguardando resposta de suporte"] = EWorkOrderSubStatusResponsible.MideaJuridico,
            ["Jurídico - suporte concluído aguardando atuação do Posto Autorizado"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Orçamento - aguardando aprovação do consumidor"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Orçamento - aprovação de orçamento para pagamento"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Orçamento - rejeição de orçamento"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Peça - material em designação ao técnico"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Peça - material entregue"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Peça - pendência de entrega"] = EWorkOrderSubStatusResponsible.MideaPlanejamento,
            ["Suporte Técnico - aguardando resposta de suporte"] = EWorkOrderSubStatusResponsible.MideaSuporteTecnico,
            ["Suporte Técnico - suporte concluído aguardando atuação do Posto Autorizado"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Técnico - reparo/visita em andamento"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Técnico - visita futura confirmada"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Triagem - execução em andamento"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Balcão - Aguardando retirada do cliente"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Balcão - Reparo concluído"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Domicílio - Atendimento realizado"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Domicílio - Consumidor ausente"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Domicílio - Dificuldade de acesso ao consumidor"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Domicílio - Orientação ao consumidor"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Domicílio - Recusa de visita pelo consumidor"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["E-Ticket - Aguardando retirada do Correios para devolução"] = EWorkOrderSubStatusResponsible.MideaCorreios,
            ["E-Ticket - Reparo concluído"] = EWorkOrderSubStatusResponsible.PostoAutorizado,

            // Categorias do fallback em inglês (closeDescription, ver
            // IServiceProviderInteractionOrderAdapter.ExtractSubStatus). Confirmado em
            // produção (2026-09-28) que muitas vêm no formato "Categoria:texto livre" — o
            // texto livre é descartado por NormalizeCategory antes de chegar aqui, por isso
            // as chaves abaixo são só a categoria fixa, sem o texto após ":".
            ["Customer request"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Creation error"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Duplicated request"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["No contact from customer"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Out of coverage"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["No defect found from triage"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Exchange - on going process"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Exchange - product in transit"] = EWorkOrderSubStatusResponsible.MideaPlanejamento,
            ["Exchange - new product order shipment pending"] = EWorkOrderSubStatusResponsible.MideaPlanejamento,
            ["Exchange cancelled"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Exchange completed"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["exchange or refund"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Refund - product collection pending"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Refund - on going process"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Refund cancelled"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Refund completed"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Parts - delivery pending"] = EWorkOrderSubStatusResponsible.MideaPlanejamento,
            ["Parts - material delivered"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Triage - execution on going"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Technician - on going repair/visit"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Technician - visit confirmed"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Installation cancelled due to external issue"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Installation Finished"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["service finished"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Order completed"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Customer - waiting schedule confirmation"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Customer - on going contact"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            ["Call Center - on going agreement negotiation"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Call Center - special Approval answered to ASP"] = EWorkOrderSubStatusResponsible.MideaCallcenter,
            ["Jira - waiting answer"] = EWorkOrderSubStatusResponsible.MideaJira,
        };

    public static EWorkOrderSubStatusResponsible Resolve(string? subStatus) =>
        subStatus is not null && Responsibles.TryGetValue(subStatus, out var responsible)
            ? responsible
            : EWorkOrderSubStatusResponsible.NaoMapeado;

    public static bool IsKnown(string subStatus) => Responsibles.ContainsKey(subStatus);

    /// <summary>
    /// Reduz o substatus bruto à sua categoria estável para agrupar/resolver responsável.
    /// O fallback em inglês (closeDescription) costuma vir como "Categoria:texto livre"
    /// (ex.: "Customer request:Cliente não deseja..."), onde o texto livre é único por OS —
    /// sem essa normalização cada OS viraria seu próprio grupo no gráfico (RF-032). Valores
    /// sem ":" (pendingReason em pt-BR, ou categorias fixas do closeDescription como
    /// "Exchange - on going process") voltam inalterados.
    /// </summary>
    public static string NormalizeCategory(string subStatus)
    {
        var colonIndex = subStatus.IndexOf(':');
        return colonIndex > 0 ? subStatus[..colonIndex].Trim() : subStatus.Trim();
    }
}
