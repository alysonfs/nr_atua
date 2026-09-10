namespace Atua.Api.Domain.Tenants;

/// <summary>
/// Regra de autoridade por peso entre papéis de membership (RF-021.3).
/// Um membership só pode alterar o papel de outro com peso estritamente
/// menor que o seu próprio.
/// </summary>
public static class ETenantMembershipRoleExtensions
{
    public static int Weight(this ETenantMembershipRole role) => role switch
    {
        ETenantMembershipRole.Owner => 100,
        ETenantMembershipRole.Admin => 50,
        ETenantMembershipRole.Default => 30,
        ETenantMembershipRole.Technical => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    public static bool CanChangeRoleOf(this ETenantMembershipRole actorRole,
        ETenantMembershipRole targetRole) => actorRole.Weight() > targetRole.Weight();
}
