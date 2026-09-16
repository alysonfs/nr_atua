namespace Atua.Api.Domain.Billing;

/// <summary>
/// Estado do vínculo entre um tenant e um plano (RF-020/ADR-024).
/// </summary>
public enum EBillingStatus
{
    Active,
    Superseded,
    Expired,
    Cancelled
}
