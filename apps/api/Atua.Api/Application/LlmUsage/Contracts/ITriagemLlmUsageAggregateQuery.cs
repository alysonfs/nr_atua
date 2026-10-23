namespace Atua.Api.Application.LlmUsage.Contracts;

/// <summary>
/// Consulta agregada de <c>TriagemLlmUsage</c> por tenant, modelo e dia
/// (fuso America/Sao_Paulo), base do relatório de consumo/custo de LLM do
/// Office (ADR-048, "Pendências futuras: Relatório de consumo por tenant no
/// Office"). A agregação é feita no banco — nenhuma linha crua de uso é
/// carregada para a aplicação, apenas os grupos (tenant × modelo × dia).
/// </summary>
public interface ITriagemLlmUsageAggregateQuery
{
    /// <param name="tenantIds">Escopo de tenants já resolvido pelo caso de uso (nunca todos os tenants do banco).</param>
    /// <param name="fromUtcInclusive">Início do período, em UTC (já convertido do dia local em America/Sao_Paulo).</param>
    /// <param name="toUtcExclusive">Fim do período (exclusivo), em UTC.</param>
    Task<IReadOnlyList<TriagemLlmUsageAggregateRow>> ExecuteAsync(IReadOnlyCollection<Guid> tenantIds,
        DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcExclusive, CancellationToken cancellationToken);
}

/// <summary>
/// Um grupo (tenant, modelo, dia local) com as somas necessárias para
/// montar totais, quebra por tenant, por dia e por modelo sem reconsultar o
/// banco.
/// </summary>
public sealed record TriagemLlmUsageAggregateRow(
    Guid TenantId,
    string ModelId,
    DateOnly LocalDate,
    int Calls,
    long InputTokens,
    long OutputTokens,
    long TotalLatencyMs,
    int FailedCalls);
