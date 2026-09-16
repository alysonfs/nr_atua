namespace Atua.Api.Domain.Integrations.CollectorControl;

/// <summary>
/// Motivo da desativação do Agente Coletor (ADR-020). <c>Manual</c> corresponde
/// a RF-008.5; os demais a RF-008.6 (perda de condição).
/// </summary>
public enum ECollectorDeactivationReason
{
    Manual,
    PlanIneligible,

    /// <summary>
    /// Timeout de re-claim: o Worker não concluiu o comando dentro do prazo
    /// configurado (ADR-021/D2). O Agente permanece Active; apenas o comando
    /// transita para Failed.
    /// </summary>
    ClaimTimeout
}
