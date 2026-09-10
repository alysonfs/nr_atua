namespace Atua.Api.Domain.Tenants;

/// <summary>
/// Papel de um membership dentro de um tenant (RF-021). A autoridade entre
/// papéis é definida por peso (<see cref="ETenantMembershipRoleExtensions.Weight"/>),
/// não por comparação direta do enum.
/// </summary>
public enum ETenantMembershipRole
{
    Owner,
    Admin,
    Default,
    Technical
}