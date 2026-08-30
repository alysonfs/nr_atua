namespace Atua.Api.Domain.Integrations.CollectorControl;

/// <summary>
/// Motivo da desativação do Agente Coletor (ADR-020). <c>Manual</c> corresponde
/// a RF-008.5; os demais a RF-008.6 (perda de condição).
/// </summary>
public enum ECollectorDeactivationReason
{
    Manual,
    TrialIneligible,
    CredentialNotValidated
}
