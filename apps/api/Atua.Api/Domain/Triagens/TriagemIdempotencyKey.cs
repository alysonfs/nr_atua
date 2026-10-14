namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Chave estável de idempotência do início da triagem, composta por tenant,
/// integração e OS (ADR-039, seção "Decisão proposta", item 1; arquitetura,
/// seção 6). Usada para que o gatilho de início da triagem seja aceito uma
/// única vez por OS elegível.
///
/// Este incremento define apenas o valor estável e sua igualdade — o
/// armazenamento/deduplicação físicos ficam para incremento futuro (ADR-039
/// não define a tabela, coleção ou índice físico).
/// </summary>
public sealed record TriagemIdempotencyKey
{
    public TriagemIdempotencyKey(Guid tenantId, Guid integrationId, string workOrderProviderId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        }

        if (integrationId == Guid.Empty)
        {
            throw new ArgumentException("IntegrationId não pode ser vazio.", nameof(integrationId));
        }

        if (string.IsNullOrWhiteSpace(workOrderProviderId))
        {
            throw new ArgumentException("WorkOrderProviderId não pode ser vazio.", nameof(workOrderProviderId));
        }

        TenantId = tenantId;
        IntegrationId = integrationId;
        WorkOrderProviderId = workOrderProviderId.Trim();
    }

    public Guid TenantId { get; }

    public Guid IntegrationId { get; }

    /// <summary>Identificador serial externo da OS no provedor (ver <see cref="Atua.Api.Domain.WorkOrders.WorkOrder.WorkOrderProviderId"/>).</summary>
    public string WorkOrderProviderId { get; }

    public override string ToString() => $"{TenantId:D}:{IntegrationId:D}:{WorkOrderProviderId}";
}
