namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Nomes físicos compartilhados entre o mapeamento EF Core de
/// <see cref="Atua.Api.Domain.Triagens.Triagem"/>
/// (<see cref="AtuaDbContext"/>) e a implementação concreta de
/// <see cref="Atua.Api.Application.Triagens.Contracts.ITriagemRepository"/>
/// (ADR-040). Mantidos em um único lugar para que a classificação da
/// violação de unicidade (SQLSTATE 23505) e a configuração do índice
/// único não possam divergir silenciosamente.
/// </summary>
internal static class TriagemPersistenceSchema
{
    /// <summary>
    /// Shadow property que guarda <c>TriagemIdempotencyKey.WorkOrderProviderId</c>
    /// na própria tabela da triagem. Preferida a uma tabela companheira
    /// separada (alternativa citada no ADR-040): o ciclo de vida dessa chave
    /// é idêntico ao do agregado — não há expiração nem reuso independente —
    /// então uma shadow property evita uma segunda tabela, um segundo Id e
    /// um join extra só para obter o mesmo resultado, sem expor a chave de
    /// idempotência como propriedade pública do domínio.
    /// </summary>
    public const string WorkOrderProviderIdShadowPropertyName = "WorkOrderProviderId";

    /// <summary>
    /// Nome da restrição única (TenantId, IntegrationId, WorkOrderProviderId)
    /// usada para arbitrar concorrência em <c>GetOrAddAsync</c> (ADR-040).
    /// </summary>
    public const string IdempotencyUniqueIndexName =
        "uq_triagens_tenant_integration_work_order_provider_id";
}
