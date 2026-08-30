namespace Atua.Api.Domain.Integrations.CollectorControl;

/// <summary>
/// Motivo da falha de um <see cref="ImmediateCollectionCommand"/> (ADR-021/D4).
///
/// Somente <see cref="CredentialRejected"/> invalida o
/// <c>ValidationStatus</c> da credencial e aciona
/// <c>ReconcileEligibilityAsync</c>. Os demais motivos transitam o comando
/// para <c>Failed</c> sem alterar a elegibilidade do Agente.
/// </summary>
public enum ECommandFailureReason
{
    CredentialRejected,
    IServiceUnavailable,
    ClaimTimeout,
    UnexpectedError
}
