namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Consulta autoritativa da OS candidata ao início da triagem, por tenant,
/// integração e identificador externo do provedor (arquitetura, seção 3,
/// itens 2/3). Usada pelo caso de uso de início idempotente da triagem para
/// resolver o <c>WorkOrderId</c> interno e validar a elegibilidade — nunca
/// confia em identificadores internos informados pelo chamador, impedindo
/// referência cruzada entre tenants/integrações e início sobre OS que não
/// pertence à combinação autenticada.
/// </summary>
public interface ITriagemEligibleWorkOrderQuery
{
    /// <summary>
    /// Retorna a OS correspondente à chave exata (tenant, integração, id
    /// externo do provedor), ou <c>null</c> quando nenhuma OS corresponde a
    /// essa combinação — inclusive quando existe uma OS com o mesmo
    /// <paramref name="workOrderProviderId"/> em outro tenant ou outra
    /// integração.
    /// </summary>
    Task<TriagemEligibleWorkOrderLookupResult?> ExecuteAsync(Guid tenantId, Guid integrationId,
        string workOrderProviderId, CancellationToken cancellationToken);
}

/// <summary>
/// Resultado mínimo necessário para resolver o <c>WorkOrderId</c> interno e
/// decidir elegibilidade de início da triagem.
/// </summary>
/// <param name="WorkOrderId">Id interno da OS — nunca aceito do chamador externo.</param>
/// <param name="Status">
/// Status cru da OS no provedor, usado exclusivamente para avaliação de
/// elegibilidade (ver <see cref="Atua.Api.Domain.Triagens.TriagemEligibleWorkOrderStatus"/>).
/// </param>
public sealed record TriagemEligibleWorkOrderLookupResult(Guid WorkOrderId, string Status);
