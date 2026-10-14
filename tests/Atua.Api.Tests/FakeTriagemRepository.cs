using System.Collections.Concurrent;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Fake thread-safe de <see cref="ITriagemRepository"/> para testes do
/// Incremento 2 (ADR-039). Usa <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// com <c>GetOrAdd</c> para garantir que, sob concorrência, exatamente uma
/// chamada observe <c>Created = true</c> para a mesma
/// <see cref="TriagemIdempotencyKey"/> — sem atrasos artificiais. Não
/// representa nem substitui um armazenamento físico de produção.
/// </summary>
internal sealed class FakeTriagemRepository : ITriagemRepository
{
    private readonly ConcurrentDictionary<TriagemIdempotencyKey, Triagem> _store = new();

    public Task<TriagemGetOrAddResult> GetOrAddAsync(TriagemIdempotencyKey key, Triagem candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var created = false;
        var stored = _store.GetOrAdd(key, _ =>
        {
            created = true;
            return candidate;
        });

        return Task.FromResult(new TriagemGetOrAddResult(stored, created));
    }
}
