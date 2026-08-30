using Atua.Api.Domain.Integrations;

namespace Atua.Api.Application.Integrations.CollectorControl;

/// <summary>
/// Avaliação centralizada de elegibilidade do Agente Coletor (ADR-020):
///
/// <code>
/// eligible = trial ativo para o tenant
///            AND credencial iService da integração com ValidationStatus == Succeeded
/// </code>
///
/// Os endpoints Office chamam este componente diretamente; a Master API não
/// chama o próprio endpoint HTTP interno (RN-008.2).
/// </summary>
public interface ICollectorEligibilityEvaluator
{
    Task<CollectorEligibility> EvaluateAsync(Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resultado da avaliação. Não expõe dados de Trial nem da credencial além da
/// decisão necessária (RN-008.6).
/// </summary>
public sealed record CollectorEligibility(bool Eligible, EActivationBlockReason BlockReason,
    EIServiceValidationStatus CredentialValidationStatus)
{
    public static CollectorEligibility Allowed(EIServiceValidationStatus status) =>
        new(true, EActivationBlockReason.None, status);

    public static CollectorEligibility Blocked(EActivationBlockReason reason,
        EIServiceValidationStatus status) => new(false, reason, status);
}
