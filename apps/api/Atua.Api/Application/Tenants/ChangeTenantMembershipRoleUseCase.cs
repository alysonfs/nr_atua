using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Tenants;

/// <summary>
/// RF-021.3: altera o papel de um membership do tenant. A autoridade é
/// definida pelo peso do papel (<see cref="ETenantMembershipRoleExtensions.Weight"/>):
/// um membership só pode alterar o papel de outro com peso estritamente
/// menor que o seu próprio. Isso garante, como efeito colateral, que o único
/// <c>Owner</c> do tenant nunca seja alterado por esta operação (RN-021.1).
/// </summary>
public sealed class ChangeTenantMembershipRoleUseCase(AtuaDbContext dbContext)
{
    public async Task<EChangeMembershipRoleStatus> ExecuteAsync(Guid actorUserId, Guid tenantId,
        Guid targetUserId, ETenantMembershipRole newRole, CancellationToken cancellationToken)
    {
        var actor = await dbContext.TenantMemberships.AsNoTracking().SingleOrDefaultAsync(
            membership => membership.UserId == actorUserId && membership.TenantId == tenantId,
            cancellationToken);
        if (actor is null) return EChangeMembershipRoleStatus.Forbidden;

        var target = await dbContext.TenantMemberships.SingleOrDefaultAsync(
            membership => membership.UserId == targetUserId && membership.TenantId == tenantId,
            cancellationToken);
        if (target is null) return EChangeMembershipRoleStatus.MembershipNotFound;

        if (!actor.Role.CanChangeRoleOf(target.Role))
        {
            return EChangeMembershipRoleStatus.Forbidden;
        }

        target.ChangeRole(newRole);
        await dbContext.SaveChangesAsync(cancellationToken);
        return EChangeMembershipRoleStatus.Success;
    }
}

public enum EChangeMembershipRoleStatus
{
    Success,
    Forbidden,
    MembershipNotFound
}
