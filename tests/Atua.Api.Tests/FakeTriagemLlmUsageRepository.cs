using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Tests;

/// <summary>
/// Fake em memória de <see cref="ITriagemLlmUsageRepository"/> (plano M2)
/// para testar <c>TriagemLlmConversationService</c> sem PostgreSQL real —
/// mesmo padrão de <see cref="FakeTriagemChannelCommandRepository"/>.
/// </summary>
internal sealed class FakeTriagemLlmUsageRepository : ITriagemLlmUsageRepository
{
    private readonly List<TriagemLlmUsageRecord> _records = new();

    public IReadOnlyList<TriagemLlmUsageRecord> Records => _records;

    public Task RecordAsync(TriagemLlmUsageRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task<int> CountCallsAsync(Guid triagemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_records.Count(record => record.TriagemId == triagemId));
    }
}
