using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Tenants;

/// <summary>
/// Orquestra a criação de tenant na primeira integração (RF-006/ADR-018):
/// valida CNPJ, cria Tenant, cria TenantMembership OWNER e associa o Trial do
/// usuário, tudo em uma única transação.
/// </summary>
public sealed class TenantOnboardingService(AtuaDbContext dbContext)
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

        var trial = await dbContext.TrialSubscriptions.SingleOrDefaultAsync(
            item => item.UserId == userId, cancellationToken);
        if (trial is null)
        {
            return CreateTenantResult.Failure(ECreateTenantStatus.TrialNotFound);
        }

        if (trial.TenantId is not null)
        {
            return CreateTenantResult.Failure(ECreateTenantStatus.UserAlreadyHasTenant);
        }

        if (dbContext.Database.IsRelational())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            return await CreateWithTransactionAsync(tenant: new Tenant(Guid.CreateVersion7(), name,
                normalizedCnpj, DefaultTimeZoneId), userId, trial, transaction, normalizedCnpj,
                cancellationToken);
        }

        return await CreateWithTransactionAsync(new Tenant(Guid.CreateVersion7(), name, normalizedCnpj,
            DefaultTimeZoneId), userId, trial, null, normalizedCnpj, cancellationToken);
    }

    private async Task<CreateTenantResult> CreateWithTransactionAsync(Tenant tenant, Guid userId,
        Domain.Billing.TrialSubscription trial,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction, string normalizedCnpj,
        CancellationToken cancellationToken)
    {
        dbContext.Tenants.Add(tenant);
        dbContext.TenantMemberships.Add(new TenantMembership(tenant.Id, userId, ETenantMembershipRole.Owner));

        try
        {
            trial.AssociateWithTenant(tenant.Id);
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

        return CreateTenantResult.Success(tenant.Id);
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
    TrialNotFound
}

public sealed record CreateTenantResult(ECreateTenantStatus Status, Guid? TenantId)
{
    public static CreateTenantResult Success(Guid tenantId) =>
        new(ECreateTenantStatus.Success, tenantId);

    public static CreateTenantResult Failure(ECreateTenantStatus status) =>
        new(status, null);
}
