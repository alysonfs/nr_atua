namespace Atua.Api.Application.LlmUsage.Contracts;

/// <summary>
/// Consulta agregada de <c>TriagemLlmUsage</c> por triagem e modelo (ADR-048
/// — mesma tabela usada pelo relatório de custo por tenant, agora agregada
/// por triagem para expor custo/tokens no pool e no detalhe da OS). A
/// agregação é feita no banco — nenhuma linha crua de uso é carregada para a
/// aplicação, apenas os grupos (triagem × modelo).
/// </summary>
public interface ITriagemLlmUsageByTriagemQuery
{
    /// <param name="triagemIds">Escopo de triagens já resolvido pelo chamador (nunca todas as triagens do banco).</param>
    Task<IReadOnlyList<TriagemLlmUsageByTriagemRow>> ExecuteAsync(IReadOnlyCollection<Guid> triagemIds,
        CancellationToken cancellationToken);
}

/// <summary>Um grupo (triagem, modelo) com as somas necessárias para montar o custo total sem reconsultar o banco.</summary>
public sealed record TriagemLlmUsageByTriagemRow(
    Guid TriagemId,
    string ModelId,
    int Calls,
    long InputTokens,
    long OutputTokens);
