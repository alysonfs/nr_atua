using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Tests;

/// <summary>
/// Fake de <see cref="ITriagemEligibleWorkOrderQuery"/> para testes do caso
/// de uso de início da triagem. Simula a consulta autoritativa à OS por
/// tenant + integração + <c>WorkOrderProviderId</c> sem depender de um banco
/// real.
/// </summary>
internal sealed class FakeTriagemEligibleWorkOrderQuery : ITriagemEligibleWorkOrderQuery
{
    private readonly Dictionary<(Guid TenantId, Guid IntegrationId, string WorkOrderProviderId),
        TriagemEligibleWorkOrderLookupResult> _workOrders = new();

    /// <summary>
    /// Quantidade de chamadas a <see cref="ExecuteAsync"/> — usado para provar
    /// que a consulta de OS não é acionada quando já existe triagem
    /// registrada para a chave (ADR-039: reentrega retorna o resultado
    /// conhecido sem reconsultar elegibilidade).
    /// </summary>
    public int CallCount { get; private set; }

    public void Seed(Guid tenantId, Guid integrationId, string workOrderProviderId, Guid workOrderId, string status) =>
        _workOrders[(tenantId, integrationId, workOrderProviderId)] =
            new TriagemEligibleWorkOrderLookupResult(workOrderId, status);

    /// <summary>Remove a OS previamente semeada, simulando que ela deixou de ser encontrada.</summary>
    public void Remove(Guid tenantId, Guid integrationId, string workOrderProviderId) =>
        _workOrders.Remove((tenantId, integrationId, workOrderProviderId));

    public Task<TriagemEligibleWorkOrderLookupResult?> ExecuteAsync(Guid tenantId, Guid integrationId,
        string workOrderProviderId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;

        return Task.FromResult(
            _workOrders.TryGetValue((tenantId, integrationId, workOrderProviderId), out var result)
                ? result
                : null);
    }
}
