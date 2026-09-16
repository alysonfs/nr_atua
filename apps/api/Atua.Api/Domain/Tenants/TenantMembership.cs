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

    public ETenantMembershipRole Role { get; private set; }

    /// <summary>
    /// Altera o papel deste membership (RF-021.3). A autoridade do agente que
    /// solicita a mudança é validada pelo chamador
    /// (<see cref="ETenantMembershipRoleExtensions.CanChangeRoleOf"/>), não por
    /// esta entidade.
    /// </summary>
    public void ChangeRole(ETenantMembershipRole newRole)
    {
        Role = newRole;
    }
}