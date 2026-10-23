namespace Atua.Api.Application.LlmUsage.Contracts;

/// <summary>
/// Relatório de consumo/custo de LLM por tenant para o Office (ADR-048,
/// pendência "Relatório de consumo por tenant no Office"). Regra de escopo:
/// não existe papel de administrador de plataforma neste sistema — todo
/// acesso é mediado por <c>TenantMembership</c> (ver
/// <c>ETenantMembershipRole</c>, <c>GetMyTenants</c> em
/// <c>TenantEndpoints</c>). Por isso o usuário autenticado só enxerga os
/// tenants dos quais é membro (qualquer papel), nunca a base inteira.
/// </summary>
public interface IGetLlmUsageSummaryUseCase
{
    Task<LlmUsageSummaryResult> ExecuteAsync(LlmUsageSummaryQuery query, CancellationToken cancellationToken);
}

/// <param name="From">Dia inicial (inclusive), em America/Sao_Paulo. Nulo = últimos 30 dias.</param>
/// <param name="To">Dia final (inclusive), em America/Sao_Paulo. Nulo = hoje em America/Sao_Paulo.</param>
public sealed record LlmUsageSummaryQuery(Guid UserId, DateOnly? From, DateOnly? To);

public enum ELlmUsageSummaryOutcome
{
    Success,

    /// <summary>Usuário autenticado não tem nenhum <c>TenantMembership</c> — não há tenant para relatar.</summary>
    Forbidden,

    /// <summary><c>From</c> posterior a <c>To</c>, ou intervalo maior que 366 dias.</summary>
    InvalidDateRange,
}

public sealed record LlmUsageSummaryResult(
    ELlmUsageSummaryOutcome Outcome,
    DateOnly From,
    DateOnly To,
    LlmUsageTotals? Totals = null,
    IReadOnlyList<LlmUsageTenantBreakdown>? Tenants = null,
    IReadOnlyList<LlmUsageDailyBreakdown>? Daily = null,
    IReadOnlyList<LlmUsageModelBreakdown>? Models = null)
{
    public static LlmUsageSummaryResult Failure(ELlmUsageSummaryOutcome outcome) => new(outcome, default, default);
}

public sealed record LlmUsageTotals(
    int Calls, long InputTokens, long OutputTokens, decimal EstimatedCost, long AvgLatencyMs, int FailedCalls);

public sealed record LlmUsageTenantBreakdown(
    Guid TenantId, string TenantName, int Calls, long InputTokens, long OutputTokens, decimal EstimatedCost,
    long AvgLatencyMs, int FailedCalls);

public sealed record LlmUsageDailyBreakdown(
    DateOnly Date, int Calls, long InputTokens, long OutputTokens, decimal EstimatedCost);

public sealed record LlmUsageModelBreakdown(
    string ModelId, int Calls, long InputTokens, long OutputTokens, decimal EstimatedCost);
