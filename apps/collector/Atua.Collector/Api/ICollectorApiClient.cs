using Atua.Collector.Contracts;

namespace Atua.Collector.Api;

/// <summary>
/// Cliente para os endpoints internos do Coletor na API Atua (RF-009).
/// </summary>
public interface ICollectorApiClient
{
    /// <summary>
    /// Reivindica o próximo comando de coleta disponível.
    /// </summary>
    /// <returns>
    /// O comando reivindicado, ou <c>null</c> se a API retornar 204 (sem comando disponível).
    /// </returns>
    Task<ClaimResponse?> ClaimAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica a elegibilidade vigente do tenant para coleta (RF-009.2/RN-009.2).
    /// </summary>
    Task<EligibilityResponse> CheckEligibilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra a conclusão de um comando de coleta.
    /// </summary>
    Task<CompleteResponse> CompleteAsync(
        Guid commandId,
        string outcome,
        ECommandFailureReason? failureReason,
        DateTimeOffset? completedAtUtc,
        CancellationToken cancellationToken = default);
}
