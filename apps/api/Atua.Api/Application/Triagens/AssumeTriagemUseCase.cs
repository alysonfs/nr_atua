using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação de <see cref="IAssumeTriagemUseCase"/>. Reaproveita
/// <see cref="ITriagemRepository"/> (ADR-040) para a concorrência otimista —
/// nunca grava diretamente via <see cref="AtuaDbContext"/>, para manter a
/// classificação de conflito (<see cref="TriagemConcurrencyConflictException"/>)
/// em um único lugar.
/// </summary>
public sealed class AssumeTriagemUseCase(
    AtuaDbContext dbContext,
    ITriagemRepository repository,
    TimeProvider timeProvider) : IAssumeTriagemUseCase
{
    public async Task<AssumeTriagemResult> ExecuteAsync(Guid tenantId, Guid triagemId, Guid requestingUserId,
        CancellationToken cancellationToken)
    {
        var isMember = await dbContext.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == requestingUserId && membership.TenantId == tenantId,
            cancellationToken);
        if (!isMember) return AssumeTriagemResult.Failure(EAssumeTriagemOutcome.Forbidden);

        var triagem = await repository.FindByIdAsync(triagemId, cancellationToken);
        if (triagem is null || triagem.TenantId != tenantId)
        {
            return AssumeTriagemResult.Failure(EAssumeTriagemOutcome.NotFound);
        }

        if (!Triagem.PoolStatuses.Contains(triagem.Status))
        {
            return new AssumeTriagemResult(EAssumeTriagemOutcome.NotInPool,
                triagem.AssumidaPorUserId, triagem.AssumidaEmUtc, triagem.Status);
        }

        try
        {
            triagem.AssumirPorOperador(requestingUserId, timeProvider.GetUtcNow());
            await repository.SaveTransitionAsync(triagem, cancellationToken);
        }
        catch (TriagemConcurrencyConflictException)
        {
            var winner = await dbContext.Triagens.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == triagemId, cancellationToken);

            return new AssumeTriagemResult(EAssumeTriagemOutcome.ConcurrencyConflict,
                winner?.AssumidaPorUserId, winner?.AssumidaEmUtc, winner?.Status);
        }

        return new AssumeTriagemResult(EAssumeTriagemOutcome.Success,
            triagem.AssumidaPorUserId, triagem.AssumidaEmUtc, triagem.Status);
    }
}
