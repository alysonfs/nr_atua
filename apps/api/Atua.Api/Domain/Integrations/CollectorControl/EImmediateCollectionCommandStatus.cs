namespace Atua.Api.Domain.Integrations.CollectorControl;

/// <summary>
/// Estados do comando de coleta imediata (ADR-020). <c>Cancelled</c> é terminal
/// e nunca volta a <c>Pending</c> (RF-008.7/RN-008.4).
///
/// Observação de escopo (RF-008): apenas <c>Pending</c> e <c>Cancelled</c> são
/// produzidos pela Master API neste portão. <c>Claimed</c>, <c>Succeeded</c> e
/// <c>Failed</c> pertencem ao contrato do Worker futuro e ficam declarados aqui
/// somente para estabilizar o vocabulário persistido.
/// </summary>
public enum EImmediateCollectionCommandStatus
{
    Pending,
    Claimed,
    Succeeded,
    Failed,
    Cancelled
}
