using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Tenants;

/// <summary>
/// Orquestra a criação de tenant na primeira integração (RF-006/ADR-018):
/// valida CNPJ, cria Tenant, cria TenantMembership OWNER, cria a Integration
/// do provedor iService (emenda "Resolução de integrationId" da ADR-018) e
/// atribui o plano trial ativo (RF-020.2/ADR-024), tudo em uma única
/// transação.
/// </summary>
public sealed class AddTenantUseCase(AtuaDbContext dbContext)
{
    private const string DefaultTimeZoneId = "America/Sao_Paulo";

    public async Task<CreateTenantResult> ExecuteAsync(Guid userId, string name, string? cnpj,
        CancellationToken cancellationToken)
    {
        var normalizedCnpj = CnpjValidator.Normalize(cnpj);
        if (normalizedCnpj is null)
        {
            return CreateTenantResult.Failure(ECreateTenantStatus.InvalidCnpj);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return CreateTenantResult.Failure(ECreateTenantStatus.InvalidCnpj);
        }

        var alreadyOwner = await dbContext.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == userId && membership.Role == ETenantMembershipRole.Owner,
            cancellationToken);
        if (alreadyOwner)
        {
            return CreateTenantResult.Failure(ECreateTenantStatus.UserAlreadyHasTenant);
        }

        var cnpjInUse = await dbContext.Tenants.AsNoTracking().AnyAsync(
            tenant => tenant.Cnpj == normalizedCnpj, cancellationToken);
        if (cnpjInUse)
        {
            return CreateTenantResult.Failure(ECreateTenantStatus.CnpjAlreadyRegistered);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user?.EmailConfirmedAt is null)
        {
            return CreateTenantResult.Failure(ECreateTenantStatus.EmailNotConfirmed);
        }

        var trialPlan = await dbContext.Plans.AsNoTracking().SingleOrDefaultAsync(
            plan => plan.Id == WellKnownPlans.TrialPlanId, cancellationToken);
        if (trialPlan is null)
        {
            // RN-020.5: estado inconsistente — o catálogo de planos deveria
            // sempre conter o plano trial seedado. Não deve ocorrer.
            return CreateTenantResult.Failure(ECreateTenantStatus.PlanCatalogInconsistent);
        }

        if (dbContext.Database.IsRelational())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            return await CreateWithTransactionAsync(tenant: new Tenant(Guid.CreateVersion7(), name,
                normalizedCnpj, DefaultTimeZoneId), userId, user.EmailConfirmedAt.Value, trialPlan,
                transaction, normalizedCnpj, cancellationToken);
        }

        return await CreateWithTransactionAsync(new Tenant(Guid.CreateVersion7(), name, normalizedCnpj,
            DefaultTimeZoneId), userId, user.EmailConfirmedAt.Value, trialPlan, null, normalizedCnpj,
            cancellationToken);
    }

    private async Task<CreateTenantResult> CreateWithTransactionAsync(Tenant tenant, Guid userId,
        DateTimeOffset emailConfirmedAt, Plan trialPlan,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction, string normalizedCnpj,
        CancellationToken cancellationToken)
    {
        dbContext.Tenants.Add(tenant);
        dbContext.TenantMemberships.Add(new TenantMembership(tenant.Id, userId, ETenantMembershipRole.Owner));

        // Emenda ADR-018 ("Resolução de integrationId"): no MVP existe
        // exatamente uma integração (iService) por tenant. A criação do
        // Tenant já cria a Integration correspondente, para que o frontend
        // obtenha o integrationId diretamente na resposta de POST
        // /api/tenants, sem exigir um endpoint de listagem/descoberta.
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id,
            WellKnownIntegrationProviders.IServiceProviderId, isEnabled: false);
        dbContext.Integrations.Add(integration);

        // RF-020.2/ADR-015: a contagem do prazo do Trial parte do instante de
        // confirmação de e-mail do usuário, não da criação do tenant, para não
        // reiniciar o prazo quando o tenant é criado depois da confirmação.
        var startsAt = emailConfirmedAt.ToUniversalTime();
        var expiresAt = trialPlan.DurationDays is null
            ? (DateTimeOffset?)null
            : new DateTimeOffset(startsAt.UtcDateTime.Date.AddDays(trialPlan.DurationDays.Value),
                TimeSpan.Zero);
        dbContext.TenantPlans.Add(new TenantPlan(Guid.CreateVersion7(), tenant.Id, trialPlan.Id, startsAt,
            expiresAt));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            if (await CnpjRegisteredConcurrentlyAsync(normalizedCnpj, cancellationToken))
            {
                return CreateTenantResult.Failure(ECreateTenantStatus.CnpjAlreadyRegistered);
            }

            throw;
        }

        return CreateTenantResult.Success(tenant.Id, integration.Id);
    }

    private async Task<bool> CnpjRegisteredConcurrentlyAsync(string cnpj, CancellationToken cancellationToken) =>
        await dbContext.Tenants.AsNoTracking().AnyAsync(tenant => tenant.Cnpj == cnpj, cancellationToken);
}

public enum ECreateTenantStatus
{
    Success,
    InvalidCnpj,
    CnpjAlreadyRegistered,
    UserAlreadyHasTenant,
    EmailNotConfirmed,
    PlanCatalogInconsistent
}

public sealed record CreateTenantResult(ECreateTenantStatus Status, Guid? TenantId, Guid? IntegrationId = null)
{
    public static CreateTenantResult Success(Guid tenantId, Guid integrationId) =>
        new(ECreateTenantStatus.Success, tenantId, integrationId);

    public static CreateTenantResult Failure(ECreateTenantStatus status) =>
        new(status, null);
}
