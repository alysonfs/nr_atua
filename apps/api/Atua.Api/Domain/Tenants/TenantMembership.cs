namespace Atua.Api.Domain.Tenants;

public sealed class TenantMembership
{
    public TenantMembership(Guid tenantId, Guid userId, ETenantMembershipRole role)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
    }

    public Guid TenantId { get; }

    public Guid UserId { get; }

    public ETenantMembershipRole Role { get; }
}