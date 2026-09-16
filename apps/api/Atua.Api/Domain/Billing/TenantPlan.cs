namespace Atua.Api.Domain.Billing;

/// <summary>
/// Vínculo entre um tenant e um <see cref="Plan"/> (RF-020/ADR-024). No máximo
/// um <c>TenantPlan</c> com <see cref="Status"/> <c>Active</c> por tenant
/// (garantido por índice filtrado único). Nunca é apagado: apenas transiciona
/// de status (RN-020.2/RN-020.5), preservando o histórico de planos do
/// tenant.
/// </summary>
public sealed class TenantPlan
{
    private TenantPlan() { }

    public TenantPlan(Guid id, Guid tenantId, Guid planId, DateTimeOffset startsAt,
        DateTimeOffset? expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        PlanId = planId;
        StartsAt = startsAt;
        ExpiresAt = expiresAt;
        Status = EBillingStatus.Active;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid PlanId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Nulo = sem expiração.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public EBillingStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Encerra o vínculo por troca de plano (RF-020.3).</summary>
    public void Supersede(DateTimeOffset now)
    {
        if (Status != EBillingStatus.Active) return;
        Status = EBillingStatus.Superseded;
        UpdatedAtUtc = now;
    }

    /// <summary>Encerra o vínculo por expiração natural (RF-020.7/RN-020.5).</summary>
    public void Expire(DateTimeOffset now)
    {
        if (Status != EBillingStatus.Active) return;
        Status = EBillingStatus.Expired;
        UpdatedAtUtc = now;
    }

    /// <summary>Encerra o vínculo por cancelamento (RN-020.4).</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != EBillingStatus.Active) return;
        Status = EBillingStatus.Cancelled;
        UpdatedAtUtc = now;
    }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is not null && ExpiresAt <= now;
}
