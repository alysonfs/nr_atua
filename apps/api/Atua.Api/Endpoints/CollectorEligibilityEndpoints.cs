using System.Security.Claims;
using Atua.Api.Application.Integrations.CollectorControl;

namespace Atua.Api.Endpoints;

/// <summary>
/// Endpoint interno consumido pelo Agente Coletor para consultar sua própria
/// elegibilidade (RF-008.2/ADR-020/ADR-024). Depende de credencial de
/// serviço com escopo <c>collector.eligibility.read</c> (RN-008.2: a Master
/// API não chama o próprio endpoint internamente, apenas
/// <see cref="ICollectorEligibilityEvaluator"/> diretamente).
/// </summary>
public static class CollectorEligibilityEndpoints
{
    public static void MapCollectorEligibilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/internal/collector/eligibility", GetCollectorEligibility)
            .RequireAuthorization("CollectorEligibility");
    }

    private static async Task<IResult> GetCollectorEligibility(ClaimsPrincipal user,
        ICollectorEligibilityEvaluator evaluator, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenantId = GetGuidClaim(user, "tenant_id");
        var integrationId = GetGuidClaim(user, "integration_id");
        if (tenantId is null || integrationId is null) return Results.Unauthorized();

        var eligibility = await evaluator.EvaluateAsync(tenantId.Value, integrationId.Value,
            cancellationToken);
        return Results.Ok(new CollectorEligibilityResponse(eligibility.Eligible, timeProvider.GetUtcNow()));
    }

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}

public sealed record CollectorEligibilityResponse(bool Eligible, DateTimeOffset EvaluatedAtUtc);
