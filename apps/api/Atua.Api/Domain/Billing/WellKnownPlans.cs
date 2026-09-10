namespace Atua.Api.Domain.Billing;

/// <summary>
/// Códigos e identificadores fixos de <see cref="Plan"/> conhecidos pelo
/// sistema no MVP (RF-020/ADR-024), seedados via <c>HasData</c>.
/// </summary>
public static class WellKnownPlans
{
    public const string TrialCode = "trial";
    public const string EssencialCode = "essencial";

    public static readonly Guid TrialPlanId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    public static readonly Guid EssencialPlanId = Guid.Parse("00000000-0000-0000-0000-0000000000f2");
}
