using System.Security.Claims;
using Atua.Api.Application.LlmUsage.Contracts;

namespace Atua.Api.Endpoints;

/// <summary>
/// Relatório de consumo/custo de LLM por tenant para o Office (ADR-048,
/// pendência "Relatório de consumo por tenant no Office"). Não existe papel
/// de administrador de plataforma neste sistema: o escopo de tenants vem
/// inteiramente de <c>TenantMembership</c> do usuário autenticado (ver
/// <see cref="IGetLlmUsageSummaryUseCase"/>), o mesmo padrão de
/// <c>/api/users/me/tenants</c>.
/// </summary>
public static class LlmUsageEndpoints
{
    public static void MapLlmUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/llm-usage/summary", GetSummaryAsync)
            .RequireAuthorization("BrowserSession");
    }

    private static async Task<IResult> GetSummaryAsync(string? from, string? to, ClaimsPrincipal user,
        IGetLlmUsageSummaryUseCase useCase, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        DateOnly? fromDate = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!DateOnly.TryParse(from, out var parsedFrom))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "invalid_from",
                    detail: "O parâmetro 'from' deve estar no formato YYYY-MM-DD.");
            }

            fromDate = parsedFrom;
        }

        DateOnly? toDate = null;
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!DateOnly.TryParse(to, out var parsedTo))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "invalid_to",
                    detail: "O parâmetro 'to' deve estar no formato YYYY-MM-DD.");
            }

            toDate = parsedTo;
        }

        var result = await useCase.ExecuteAsync(new LlmUsageSummaryQuery(userId.Value, fromDate, toDate),
            cancellationToken);

        return result.Outcome switch
        {
            ELlmUsageSummaryOutcome.Success => Results.Ok(ToResponse(result)),

            ELlmUsageSummaryOutcome.Forbidden => Results.Forbid(),

            ELlmUsageSummaryOutcome.InvalidDateRange => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "invalid_date_range",
                detail: "'from' deve ser menor ou igual a 'to', e o intervalo não pode ultrapassar 366 dias."),

            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static LlmUsageSummaryResponse ToResponse(LlmUsageSummaryResult result) => new(
        From: result.From.ToString("yyyy-MM-dd"),
        To: result.To.ToString("yyyy-MM-dd"),
        Currency: "USD",
        Totals: new LlmUsageTotalsResponse(
            result.Totals!.Calls, result.Totals.InputTokens, result.Totals.OutputTokens,
            result.Totals.EstimatedCost, result.Totals.AvgLatencyMs, result.Totals.FailedCalls),
        Tenants: result.Tenants!.Select(tenant => new LlmUsageTenantResponse(
            tenant.TenantId, tenant.TenantName, tenant.Calls, tenant.InputTokens, tenant.OutputTokens,
            tenant.EstimatedCost, tenant.AvgLatencyMs, tenant.FailedCalls)).ToArray(),
        Daily: result.Daily!.Select(day => new LlmUsageDailyResponse(
            day.Date.ToString("yyyy-MM-dd"), day.Calls, day.InputTokens, day.OutputTokens, day.EstimatedCost))
            .ToArray(),
        Models: result.Models!.Select(model => new LlmUsageModelResponse(
            model.ModelId, model.Calls, model.InputTokens, model.OutputTokens, model.EstimatedCost)).ToArray());

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string claimType) =>
        Guid.TryParse(user.FindFirstValue(claimType), out var value) ? value : null;
}

public sealed record LlmUsageSummaryResponse(
    string From,
    string To,
    string Currency,
    LlmUsageTotalsResponse Totals,
    IReadOnlyList<LlmUsageTenantResponse> Tenants,
    IReadOnlyList<LlmUsageDailyResponse> Daily,
    IReadOnlyList<LlmUsageModelResponse> Models);

public sealed record LlmUsageTotalsResponse(
    int Calls, long InputTokens, long OutputTokens, decimal EstimatedCost, long AvgLatencyMs, int FailedCalls);

public sealed record LlmUsageTenantResponse(
    Guid TenantId, string TenantName, int Calls, long InputTokens, long OutputTokens, decimal EstimatedCost,
    long AvgLatencyMs, int FailedCalls);

public sealed record LlmUsageDailyResponse(
    string Date, int Calls, long InputTokens, long OutputTokens, decimal EstimatedCost);

public sealed record LlmUsageModelResponse(
    string ModelId, int Calls, long InputTokens, long OutputTokens, decimal EstimatedCost);
