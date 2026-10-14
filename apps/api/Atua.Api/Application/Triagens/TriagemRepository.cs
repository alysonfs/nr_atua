using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação concreta de <see cref="ITriagemRepository"/> sobre o
/// PostgreSQL da Master API, via <see cref="AtuaDbContext"/> (ADR-040).
///
/// Caminho rápido: consulta a triagem já persistida para a chave antes de
/// tentar inserir. Sob concorrência real, duas instâncias podem passar pela
/// consulta sem encontrar nada e tentar inserir ao mesmo tempo — nesse caso a
/// arbitragem final é da restrição única
/// <see cref="TriagemPersistenceSchema.IdempotencyUniqueIndexName"/> no
/// banco, nunca de um lock em memória (ADR-040, "Decisão proposta").
/// </summary>
public sealed class TriagemRepository(AtuaDbContext dbContext) : ITriagemRepository
{
    public async Task<TriagemGetOrAddResult> GetOrAddAsync(TriagemIdempotencyKey key, Triagem candidate,
        CancellationToken cancellationToken)
    {
        var existing = await FindByKeyAsync(key, cancellationToken);
        if (existing is not null)
        {
            return new TriagemGetOrAddResult(existing, Created: false);
        }

        dbContext.Triagens.Add(candidate);
        dbContext.Entry(candidate)
            .Property(TriagemPersistenceSchema.WorkOrderProviderIdShadowPropertyName)
            .CurrentValue = key.WorkOrderProviderId;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new TriagemGetOrAddResult(candidate, Created: true);
        }
        catch (DbUpdateException exception) when (IsIdempotencyUniqueViolation(exception))
        {
            // Perdeu a corrida: outra chamada concorrente já inseriu a
            // triagem para a mesma chave. Remove a candidata rejeitada do
            // change tracker — ela nunca foi persistida — e retorna a
            // instância vencedora, sem reexecutar nenhuma transição sobre a
            // candidata descartada.
            dbContext.Entry(candidate).State = EntityState.Detached;

            var persisted = await FindByKeyAsync(key, cancellationToken);
            if (persisted is null)
            {
                // Não deveria ser alcançável: a restrição só dispara quando já
                // existe linha com a mesma chave. Propositalmente não
                // mascarado como "Created" — sinaliza inconsistência real.
                throw new InvalidOperationException(
                    "Violação da restrição de idempotência da triagem detectada, mas nenhum registro foi " +
                    $"encontrado para a chave '{key}'.");
            }

            return new TriagemGetOrAddResult(persisted, Created: false);
        }
    }

    public Task<Triagem?> FindByKeyAsync(TriagemIdempotencyKey key, CancellationToken cancellationToken) =>
        dbContext.Triagens.FirstOrDefaultAsync(triagem =>
                triagem.TenantId == key.TenantId
                && triagem.IntegrationId == key.IntegrationId
                && EF.Property<string>(triagem, TriagemPersistenceSchema.WorkOrderProviderIdShadowPropertyName)
                    == key.WorkOrderProviderId,
            cancellationToken);

    /// <summary>
    /// Classifica exclusivamente a violação da restrição única de
    /// idempotência da triagem (SQLSTATE 23505 +
    /// <see cref="TriagemPersistenceSchema.IdempotencyUniqueIndexName"/>).
    /// Qualquer outro <see cref="DbUpdateException"/> — incluindo outras
    /// violações 23505 de restrições diferentes — não é capturado aqui e
    /// deve propagar (ADR-040).
    ///
    /// Internal — visível para <c>Atua.Api.Tests</c> via
    /// <c>InternalsVisibleTo</c> — apenas para permitir teste direto do
    /// classificador sem expor um banco real (ver
    /// <c>TriagemRepositoryTests</c>) nem ampliar a API pública.
    /// </summary>
    internal static bool IsIdempotencyUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && postgresException.ConstraintName == TriagemPersistenceSchema.IdempotencyUniqueIndexName;
}
