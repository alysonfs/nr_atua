namespace Atua.Collector.Contracts;

/// <summary>
/// Resposta do endpoint <c>GET /api/internal/collector/eligibility</c> (RF-009.2/RN-009.2).
/// </summary>
public sealed record EligibilityResponse(
    bool Eligible,
    DateTimeOffset EvaluatedAtUtc);
