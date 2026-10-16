using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação concreta de <see cref="ITriagemEligibleWorkOrderQuery"/>
/// sobre o PostgreSQL da Master API, via <see cref="AtuaDbContext"/>, no
/// mesmo padrão dos demais query handlers de leitura (ver
/// <c>Atua.Api.Application.WorkOrders.WorkOrderDetailQueryHandler</c>).
///
/// A identidade de uma OS é (TenantId, WorkOrderProviderId) — ver
/// <see cref="Atua.Api.Domain.WorkOrders.WorkOrder"/>, RN-017.6. A consulta
/// também filtra por <c>IntegrationId</c> para impedir que uma OS seja
/// alcançada por uma credencial de serviço autenticada para outra
/// integração do mesmo tenant.
/// </summary>
public sealed class TriagemEligibleWorkOrderQueryHandler(AtuaDbContext dbContext) : ITriagemEligibleWorkOrderQuery
{
    public Task<TriagemEligibleWorkOrderLookupResult?> ExecuteAsync(Guid tenantId, Guid integrationId,
        string workOrderProviderId, CancellationToken cancellationToken) =>
        dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.TenantId == tenantId
                && order.IntegrationId == integrationId
                && order.WorkOrderProviderId == workOrderProviderId)
            .Select(order => new TriagemEligibleWorkOrderLookupResult(
                order.Id,
                order.Status,
                order.WorkOrderProviderId,
                order.WorkOrderProviderNo,
                order.ProductBrand,
                order.ProductModel))
            .SingleOrDefaultAsync(cancellationToken);
}
