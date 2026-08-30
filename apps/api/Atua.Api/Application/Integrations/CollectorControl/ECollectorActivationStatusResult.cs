namespace Atua.Api.Application.Integrations.CollectorControl;

public enum ECollectorActivationStatusResult
{
    Success,
    Forbidden,
    IntegrationNotFound,
    NotEligible,
    MissingIdempotencyKey,
    IdempotencyKeyConflict
}
