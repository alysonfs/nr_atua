using Atua.Api.Domain.Billing;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Billing;

/// <summary>
/// Serviço para criar Trial após confirmação de email.
/// </summary>
public sealed class CreateTrialService(
    AtuaDbContext dbContext,
    TimeProvider _timeProvider)
{
    /// <summary>
    /// Cria um Trial para o usuário após confirmação de email.
    ///
    /// Regra: o vencimento é 00:00 UTC da data UTC da confirmação acrescida de
    /// sete dias. Um usuário possui no máximo um Trial, inclusive após expirar.
    /// </summary>
    public async Task<CreateTrialResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_timeProvider);
        var user = await dbContext.Users.FindAsync(new object?[] { userId }, cancellationToken);

        if (user is null)
        {
            throw new InvalidOperationException("Usuário não encontrado.");
        }

        if (user.EmailConfirmedAt is null)
        {
            throw new InvalidOperationException("E-mail do usuário não foi confirmado.");
        }

        // A idempotência não depende de o Trial ainda estar ativo: uma nova
        // confirmação jamais pode reiniciar o prazo.
        var existingTrial = await dbContext.TrialSubscriptions
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingTrial is not null)
        {
            // Idempotência: retorna o Trial existente
            return new CreateTrialResult(existingTrial.Id, existingTrial.ExpiresAt);
        }

        var trial = await AddForConfirmedUserAsync(user, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateTrialResult(trial.Id, trial.ExpiresAt);
    }

    /// <summary>
    /// Inclui o Trial no contexto sem persistir. O fluxo de confirmação usa este
    /// método para persistir a confirmação e o Trial na mesma transação do EF.
    /// </summary>
    public async Task<TrialSubscription> AddForConfirmedUserAsync(
        Domain.Identity.User user,
        CancellationToken cancellationToken)
    {
        if (user.EmailConfirmedAt is null)
        {
            throw new InvalidOperationException("E-mail do usuário não foi confirmado.");
        }

        var existingTrial = await dbContext.TrialSubscriptions
            .FirstOrDefaultAsync(t => t.UserId == user.Id, cancellationToken);

        if (existingTrial is not null)
        {
            return existingTrial;
        }

        var startsAt = user.EmailConfirmedAt.Value.ToUniversalTime();
        var expiresAt = new DateTimeOffset(
            startsAt.UtcDateTime.Date.AddDays(7),
            TimeSpan.Zero);

        var trial = new TrialSubscription(
            Guid.CreateVersion7(),
            user.Id,
            startsAt,
            expiresAt);

        dbContext.TrialSubscriptions.Add(trial);
        return trial;
    }

    /// <summary>
    /// Associa um Trial existente a um Tenant, sem alterar sua validade.
    /// </summary>
    public async Task AssociateToTenantAsync(
        Guid trialId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var trial = await dbContext.TrialSubscriptions.FindAsync(
            new object?[] { trialId },
            cancellationToken);

        if (trial is null)
        {
            throw new InvalidOperationException("Trial não encontrado.");
        }

        // A associação preserva as datas e também é permitida após a expiração.
        // A elegibilidade de coleta é avaliada separadamente.
        // Validar que ainda não foi associado
        if (trial.TenantId.HasValue)
        {
            throw new InvalidOperationException("Trial já foi associado a um Tenant.");
        }

        // Associar sem alterar validade
        trial.AssociateWithTenant(tenantId);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Resultado da operação de criação de Trial.
/// </summary>
public sealed record CreateTrialResult(Guid TrialId, DateTimeOffset ExpiresAtUtc);
