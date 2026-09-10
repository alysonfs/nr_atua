using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Integrations.CollectorControl;

/// <summary>
/// Serviço de aplicação do controle de ativação do Agente Coletor
/// (RF-008/ADR-020).
///
/// Escopo deste portão: apenas controle de estado e criação do comando de
/// coleta imediata em <c>Pending</c>. Nada executa o comando; não há acesso ao
/// iService, scraping ou Worker aqui.
/// </summary>
public sealed class CollectorActivationService(
    AtuaDbContext dbContext,
    ICollectorEligibilityEvaluator eligibilityEvaluator,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    /// <summary>
    /// Leitura sanitizada do estado (RF-008.8). Retorna <c>null</c> quando o
    /// usuário não possui membership ativo no tenant, para não revelar
    /// existência de recursos de outro tenant (RF-008.2/RN-008.1).
    /// </summary>
    public async Task<CollectorActivationView?> GetAsync(Guid userId, Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken)
    {
        if (!await HasControlRoleAsync(userId, tenantId, cancellationToken)) return null;

        var integration = await FindIntegrationAsync(tenantId, integrationId, cancellationToken);
        if (integration is null) return null;

        var activation = await dbContext.CollectorActivations.AsNoTracking().SingleOrDefaultAsync(
            item => item.IntegrationId == integrationId && item.TenantId == tenantId, cancellationToken);
        var eligibility = await eligibilityEvaluator.EvaluateAsync(tenantId, integrationId, cancellationToken);
        var lastCommand = await FindLastCommandAsync(tenantId, integrationId, cancellationToken);

        return BuildView(activation, eligibility, lastCommand);
    }

    /// <summary>
    /// Ativa o Agente e cria exatamente um comando imediato <c>Pending</c>
    /// (RF-008.4/RN-008.4).
    /// </summary>
    public async Task<CollectorActivationResult> ActivateAsync(Guid userId, Guid tenantId,
        Guid integrationId, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return CollectorActivationResult.Failure(ECollectorActivationStatusResult.MissingIdempotencyKey);
        }

        if (!await HasControlRoleAsync(userId, tenantId, cancellationToken))
        {
            return CollectorActivationResult.Failure(ECollectorActivationStatusResult.Forbidden);
        }

        var integration = await FindIntegrationAsync(tenantId, integrationId, cancellationToken);
        if (integration is null)
        {
            return CollectorActivationResult.Failure(ECollectorActivationStatusResult.IntegrationNotFound);
        }

        var replay = await TryReplayAsync(tenantId, integrationId, userId,
            ECollectorControlOperation.Activate, idempotencyKey!, cancellationToken);
        if (replay is not null) return replay;

        var eligibility = await eligibilityEvaluator.EvaluateAsync(tenantId, integrationId, cancellationToken);
        if (!eligibility.Eligible)
        {
            // RF-008.3/RN-008.3: nenhum comando é criado e o Agente permanece
            // Desativado.
            return CollectorActivationResult.Failure(ECollectorActivationStatusResult.NotEligible,
                eligibility.BlockReason);
        }

        var now = timeProvider.GetUtcNow();
        var activation = await GetOrCreateActivationAsync(tenantId, integrationId, cancellationToken);
        var pending = await FindPendingCommandAsync(tenantId, integrationId, cancellationToken);

        if (activation.Status != ECollectorActivationStatus.Active)
        {
            activation.Activate(now);
        }

        // RN-008.4: exatamente um comando Pending por integração. Uma ativação
        // repetida releia o pendente existente em vez de criar outro; o índice
        // único parcial é a garantia final contra concorrência.
        if (pending is null)
        {
            pending = new ImmediateCollectionCommand(Guid.CreateVersion7(), tenantId, integrationId,
                integration.ProviderId, now);
            dbContext.ImmediateCollectionCommands.Add(pending);
        }

        var view = BuildView(activation, eligibility, pending);
        RecordIdempotency(tenantId, integrationId, userId, ECollectorControlOperation.Activate,
            idempotencyKey!, view, now);

        await dbContext.SaveChangesAsync(cancellationToken);
        return CollectorActivationResult.Success(view);
    }

    /// <summary>
    /// Desativação manual (RF-008.5): impede novas execuções e cancela o
    /// comando Pendente.
    /// </summary>
    public async Task<CollectorActivationResult> DeactivateAsync(Guid userId, Guid tenantId,
        Guid integrationId, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return CollectorActivationResult.Failure(ECollectorActivationStatusResult.MissingIdempotencyKey);
        }

        if (!await HasControlRoleAsync(userId, tenantId, cancellationToken))
        {
            return CollectorActivationResult.Failure(ECollectorActivationStatusResult.Forbidden);
        }

        var integration = await FindIntegrationAsync(tenantId, integrationId, cancellationToken);
        if (integration is null)
        {
            return CollectorActivationResult.Failure(ECollectorActivationStatusResult.IntegrationNotFound);
        }

        var replay = await TryReplayAsync(tenantId, integrationId, userId,
            ECollectorControlOperation.Deactivate, idempotencyKey!, cancellationToken);
        if (replay is not null) return replay;

        var now = timeProvider.GetUtcNow();
        var activation = await GetOrCreateActivationAsync(tenantId, integrationId, cancellationToken);
        activation.Deactivate(ECollectorDeactivationReason.Manual, now);

        var pending = await FindPendingCommandAsync(tenantId, integrationId, cancellationToken);
        CancelPending(pending, ECollectorDeactivationReason.Manual, now);

        var eligibility = await eligibilityEvaluator.EvaluateAsync(tenantId, integrationId, cancellationToken);
        // Usa a instância recém-cancelada quando existir: a leitura AsNoTracking
        // ainda enxergaria o estado anterior antes do SaveChanges.
        var lastCommand = pending
            ?? await FindLastCommandAsync(tenantId, integrationId, cancellationToken);
        var view = BuildView(activation, eligibility, lastCommand);
        RecordIdempotency(tenantId, integrationId, userId, ECollectorControlOperation.Deactivate,
            idempotencyKey!, view, now);

        await dbContext.SaveChangesAsync(cancellationToken);
        return CollectorActivationResult.Success(view);
    }

    /// <summary>
    /// Desativação por perda de condição (RF-008.6/RN-008.5). Deve ser chamada
    /// na mesma transação que persiste a alteração capaz de tornar a
    /// elegibilidade falsa (ADR-020). Não chama <c>SaveChanges</c>: quem
    /// persiste a alteração é o responsável pela transação.
    /// </summary>
    public async Task ReconcileEligibilityAsync(Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken)
    {
        var activation = await dbContext.CollectorActivations.SingleOrDefaultAsync(
            item => item.IntegrationId == integrationId && item.TenantId == tenantId, cancellationToken);
        if (activation is null) return;

        var eligibility = await eligibilityEvaluator.EvaluateAsync(tenantId, integrationId, cancellationToken);
        if (eligibility.Eligible) return;

        // RF-008.7: um comando cancelado nunca é reativado automaticamente;
        // recuperar a elegibilidade não reativa o Agente. ADR-024: o único
        // motivo de bloqueio restante é o plano do tenant.
        var reason = ECollectorDeactivationReason.PlanIneligible;

        var now = timeProvider.GetUtcNow();
        if (activation.Status == ECollectorActivationStatus.Active)
        {
            activation.Deactivate(reason, now);
        }

        CancelPending(await FindPendingCommandAsync(tenantId, integrationId, cancellationToken), reason, now);
    }

    private static void CancelPending(ImmediateCollectionCommand? pending,
        ECollectorDeactivationReason reason, DateTimeOffset now) => pending?.Cancel(reason, now);

    private async Task<CollectorActivationResult?> TryReplayAsync(Guid tenantId, Guid integrationId,
        Guid userId, ECollectorControlOperation operation, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.CollectorControlIdempotencies.AsNoTracking().SingleOrDefaultAsync(
            item => item.IntegrationId == integrationId && item.Operation == operation &&
                    item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (record is null) return null;

        var expectedHash = ComputeRequestHash(tenantId, integrationId, userId, operation);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(record.RequestHash), Encoding.UTF8.GetBytes(expectedHash)))
        {
            // ADR-020: reusar a chave com conteúdo diferente devolve conflito,
            // sem executar nova mutação.
            return CollectorActivationResult.Failure(ECollectorActivationStatusResult.IdempotencyKeyConflict);
        }

        var view = JsonSerializer.Deserialize<CollectorActivationView>(record.ResponseSnapshot);
        return view is null ? null : CollectorActivationResult.Success(view);
    }

    private void RecordIdempotency(Guid tenantId, Guid integrationId, Guid userId,
        ECollectorControlOperation operation, string idempotencyKey, CollectorActivationView view,
        DateTimeOffset now) =>
        dbContext.CollectorControlIdempotencies.Add(new CollectorControlIdempotency(
            Guid.CreateVersion7(), tenantId, integrationId, operation, idempotencyKey,
            ComputeRequestHash(tenantId, integrationId, userId, operation),
            JsonSerializer.Serialize(view), now, now.Add(IdempotencyRetention)));

    private static string ComputeRequestHash(Guid tenantId, Guid integrationId, Guid userId,
        ECollectorControlOperation operation) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{operation}:{tenantId}:{integrationId}:{userId}")));

    private async Task<CollectorActivation> GetOrCreateActivationAsync(Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken)
    {
        var activation = await dbContext.CollectorActivations.SingleOrDefaultAsync(
            item => item.IntegrationId == integrationId && item.TenantId == tenantId, cancellationToken);
        if (activation is not null) return activation;

        // RF-008.1: o estado inicial é Desativado.
        activation = new CollectorActivation(Guid.CreateVersion7(), tenantId, integrationId);
        dbContext.CollectorActivations.Add(activation);
        return activation;
    }

    private Task<Domain.Integrations.Integration?> FindIntegrationAsync(Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken) =>
        dbContext.Integrations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == integrationId && item.TenantId == tenantId, cancellationToken);

    private Task<ImmediateCollectionCommand?> FindPendingCommandAsync(Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken) =>
        dbContext.ImmediateCollectionCommands.SingleOrDefaultAsync(
            item => item.IntegrationId == integrationId && item.TenantId == tenantId &&
                    item.Status == EImmediateCollectionCommandStatus.Pending, cancellationToken);

    private async Task<ImmediateCollectionCommand?> FindLastCommandAsync(Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken) =>
        await dbContext.ImmediateCollectionCommands.AsNoTracking()
            .Where(item => item.IntegrationId == integrationId && item.TenantId == tenantId)
            .OrderByDescending(item => item.RequestedAtUtc).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// RF-008.2: somente membership ativo <c>OWNER</c> ou <c>ADMIN</c> no
    /// tenant da rota pode ler ou alterar o estado.
    /// </summary>
    private Task<bool> HasControlRoleAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.TenantMemberships.AsNoTracking().AnyAsync(membership =>
            membership.UserId == userId && membership.TenantId == tenantId &&
            (membership.Role == ETenantMembershipRole.Owner ||
             membership.Role == ETenantMembershipRole.Admin), cancellationToken);

    private static CollectorActivationView BuildView(CollectorActivation? activation,
        CollectorEligibility eligibility, ImmediateCollectionCommand? lastCommand) =>
        new(
            (activation?.Status ?? ECollectorActivationStatus.Inactive).ToString(),
            eligibility.Eligible,
            eligibility.BlockReason.ToString(),
            eligibility.CredentialValidationStatus.ToString(),
            activation?.Status == ECollectorActivationStatus.Active ? activation.ActivatedAtUtc : null,
            activation?.DeactivatedAtUtc,
            lastCommand is null
                ? null
                : new ImmediateCommandView(lastCommand.Id, lastCommand.Status.ToString(),
                    lastCommand.RequestedAtUtc));
}

/// <summary>
/// DTO sanitizado de leitura/mutação (ADR-020). Nunca contém credenciais,
/// cookies, tokens, URLs sensíveis ou dados de Trial além da decisão.
/// </summary>
public sealed record CollectorActivationView(string Status, bool CanActivate,
    string ActivationBlockReason, string CredentialValidationStatus,
    DateTimeOffset? ActivatedAtUtc, DateTimeOffset? DeactivatedAtUtc,
    ImmediateCommandView? LastImmediateCommand);

public sealed record ImmediateCommandView(Guid CommandId, string Status,
    DateTimeOffset RequestedAtUtc);

public sealed record CollectorActivationResult(ECollectorActivationStatusResult Status,
    CollectorActivationView? View, EActivationBlockReason BlockReason)
{
    public static CollectorActivationResult Success(CollectorActivationView view) =>
        new(ECollectorActivationStatusResult.Success, view, EActivationBlockReason.None);

    public static CollectorActivationResult Failure(ECollectorActivationStatusResult status,
        EActivationBlockReason blockReason = EActivationBlockReason.None) =>
        new(status, null, blockReason);
}
