using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Persiste o registro de uso de LLM por chamada (ADR-048) — base da
/// cobrança por tenant. Toda chamada a <see cref="ITriagemLlmClient"/> deve
/// ser registrada, inclusive as que resultam em
/// <see cref="ETriagemLlmOutcome.InvalidOutput"/> ou
/// <see cref="ETriagemLlmOutcome.ProviderUnavailable"/>.
/// </summary>
public interface ITriagemLlmUsageRepository
{
    Task RecordAsync(TriagemLlmUsageRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Conta quantas chamadas de LLM já foram registradas para a triagem
    /// (plano M2, §3/§10 — orçamento de turnos por triagem). Reaproveita a
    /// tabela de uso existente em vez de um contador dedicado: toda chamada,
    /// inclusive as que falham, é contada para o orçamento.
    /// </summary>
    Task<int> CountCallsAsync(Guid triagemId, CancellationToken cancellationToken);
}

/// <summary>Comando para registrar uma chamada de LLM. <see cref="ModelId"/> é vazio quando a chamada nem chegou a ser feita ao fornecedor.</summary>
public sealed record TriagemLlmUsageRecord(
    Guid TenantId,
    Guid TriagemId,
    string ModelId,
    int InputTokens,
    int OutputTokens,
    long LatencyMs,
    ETriagemLlmOutcome Outcome,
    DateTimeOffset CreatedAtUtc);
