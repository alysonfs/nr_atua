namespace Atua.Api.Domain.Billing;

public sealed class TrialSubscription
{
    public TrialSubscription(Guid id, Guid userId, DateTimeOffset startsAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        StartsAt = startsAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public Guid? TenantId { get; private set; }

    public DateTimeOffset StartsAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public void AssociateWithTenant(Guid tenantId)
    {
        if (TenantId is not null)
        {
            throw new InvalidOperationException("Trial ja associado a um tenant.");
        }

        TenantId = tenantId;
    }
}