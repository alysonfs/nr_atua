using System.Reflection;
using System.Runtime.CompilerServices;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Fake thread-safe de <see cref="ITriagemRepository"/> (ADR-039/ADR-040).
///
/// Diferente de um simples dicionário com instâncias compartilhadas, este
/// fake devolve um CLONE independente a cada <see cref="GetOrAddAsync"/>/
/// <see cref="FindByKeyAsync"/> — exatamente como uma consulta real ao
/// PostgreSQL materializaria uma instância própria por conexão/DbContext.
/// Isso é necessário porque, desde que <c>StartTriagemUseCase</c> passou a
/// mutar o agregado (<c>MarcarContatoIniciado</c>/<c>MarcarFalhaDeContato</c>)
/// entre chamadas ao canal, duas execuções concorrentes do caso de uso NÃO
/// podem compartilhar a mesma referência mutável — faria uma mutar o estado
/// que a outra está checando, produzindo <see cref="InvalidTriagemTransitionException"/>
/// espúria, nunca observável contra um banco real.
///
/// <see cref="SaveTransitionAsync"/> arbitra a concorrência comparando o
/// <see cref="Triagem.ConcurrencyToken"/> capturado no momento da leitura
/// (<c>_originalTokens</c>) contra o token atualmente "committed" para a
/// chave — mesma semântica da restrição <c>IsConcurrencyToken()</c> usada
/// por <c>TriagemRepository</c> real. Em divergência, lança
/// <see cref="TriagemConcurrencyConflictException"/>, nunca sobrescreve
/// silenciosamente.
/// </summary>
internal sealed class FakeTriagemRepository : ITriagemRepository
{
    private readonly Dictionary<TriagemIdempotencyKey, Triagem> _committed = new();
    private readonly Dictionary<Guid, TriagemIdempotencyKey> _keysById = new();
    private readonly Dictionary<Guid, TriagemChannelCommand> _channelCommandsByTriagemId = new();
    private readonly ConditionalWeakTable<Triagem, StrongBox<Guid>> _originalTokens = new();
    private readonly object _lock = new();

    /// <summary>
    /// Comandos de canal persistidos via <see cref="GetOrAddWithChannelCommandAsync"/>,
    /// indexados pela triagem a que pertencem — para asserções de teste
    /// (ex.: verificar que o comando foi criado atomicamente com a triagem).
    /// </summary>
    public IReadOnlyDictionary<Guid, TriagemChannelCommand> ChannelCommandsByTriagemId => _channelCommandsByTriagemId;

    /// <summary>
    /// Quando maior que zero, as próximas chamadas a <see cref="SaveTransitionAsync"/>
    /// lançam <see cref="TriagemConcurrencyConflictException"/> (decrementando
    /// o contador a cada chamada), simulando deterministicamente que outro
    /// processo já persistiu uma transição concorrente para a mesma triagem
    /// — útil para testar o branch de "recarregar o vencedor sem repetir a
    /// chamada ao canal" sem precisar orquestrar threads reais.
    /// </summary>
    public int ConflictsToSimulateOnSaveTransition { get; set; }

    public int SaveTransitionCallCount { get; private set; }

    public Task<TriagemGetOrAddResult> GetOrAddAsync(TriagemIdempotencyKey key, Triagem candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_committed.TryGetValue(key, out var existing))
            {
                return Task.FromResult(new TriagemGetOrAddResult(Snapshot(existing), Created: false));
            }

            _committed[key] = candidate;
            _keysById[candidate.Id] = key;
            return Task.FromResult(new TriagemGetOrAddResult(Snapshot(candidate), Created: true));
        }
    }

    public Task<Triagem?> FindByKeyAsync(TriagemIdempotencyKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult(_committed.TryGetValue(key, out var existing) ? Snapshot(existing) : null);
        }
    }

    /// <summary>
    /// Diferente de <see cref="FindByKeyAsync"/>/<see cref="GetOrAddAsync"/>
    /// (que clonam, simulando leituras independentes de um banco real), este
    /// método devolve a própria instância "committed" sem clonar. Ele
    /// simula o mesmo <c>DbContext</c> compartilhado por requisição que
    /// <c>TriagemChannelCommandService</c> usa em produção: a triagem
    /// referenciada por um comando de canal é consultada e mutada na MESMA
    /// unidade de trabalho que persiste o comando — uma única
    /// <c>SaveChangesAsync</c> (do repositório de comando) grava ambas as
    /// mutações, exatamente como o EF Core rastrearia a mesma entidade
    /// anexada ao contexto. Clonar aqui quebraria essa semântica: a mutação
    /// aplicada pelo chamador nunca seria observável sem uma chamada
    /// adicional e inexistente de <see cref="SaveTransitionAsync"/>.
    /// </summary>
    public Task<Triagem?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_keysById.TryGetValue(id, out var key) || !_committed.TryGetValue(key, out var existing))
            {
                return Task.FromResult<Triagem?>(null);
            }

            return Task.FromResult<Triagem?>(existing);
        }
    }

    public Task<TriagemGetOrAddResult> GetOrAddWithChannelCommandAsync(TriagemIdempotencyKey key, Triagem candidate,
        TriagemChannelCommand channelCommand, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_committed.TryGetValue(key, out var existing))
            {
                // Conflito de criação concorrente: a triagem já existe —
                // nenhum comando de canal é persistido (mesma semântica do
                // repositório real, que descarta o candidato/comando
                // perdedores sem efeito colateral).
                return Task.FromResult(new TriagemGetOrAddResult(Snapshot(existing), Created: false));
            }

            _committed[key] = candidate;
            _keysById[candidate.Id] = key;
            _channelCommandsByTriagemId[candidate.Id] = channelCommand;
            return Task.FromResult(new TriagemGetOrAddResult(Snapshot(candidate), Created: true));
        }
    }

    public Task SaveTransitionAsync(Triagem triagem, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            SaveTransitionCallCount++;

            if (ConflictsToSimulateOnSaveTransition > 0)
            {
                ConflictsToSimulateOnSaveTransition--;
                throw new TriagemConcurrencyConflictException(triagem.Id,
                    new InvalidOperationException("Conflito de concorrência simulado."));
            }

            if (!_keysById.TryGetValue(triagem.Id, out var key) || !_committed.TryGetValue(key, out var committed))
            {
                throw new InvalidOperationException(
                    $"Triagem '{triagem.Id}' não encontrada para persistir a transição.");
            }

            if (!_originalTokens.TryGetValue(triagem, out var originalToken)
                || committed.ConcurrencyToken != originalToken.Value)
            {
                // O estado "committed" mudou desde que esta instância foi
                // lida (outra execução concorrente já persistiu uma
                // transição) — mesma semântica de DbUpdateConcurrencyException
                // do provedor real.
                throw new TriagemConcurrencyConflictException(triagem.Id,
                    new InvalidOperationException(
                        "Conflito de concorrência: o estado persistido mudou desde a leitura."));
            }

            _committed[key] = triagem;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Devolve um clone independente de <paramref name="source"/> e registra
    /// o <see cref="Triagem.ConcurrencyToken"/> original (no momento desta
    /// leitura) associado à instância clonada, para arbitragem posterior em
    /// <see cref="SaveTransitionAsync"/>.
    /// </summary>
    private Triagem Snapshot(Triagem source)
    {
        var clone = DeepClone(source);
        _originalTokens.AddOrUpdate(clone, new StrongBox<Guid>(source.ConcurrencyToken));
        return clone;
    }

    /// <summary>
    /// Clone por reflexão de todos os campos de instância de
    /// <see cref="Triagem"/> (inclusive o dicionário privado de tentativas
    /// inválidas, copiado por valor para que mutações no clone nunca vazem
    /// para o estado "committed"). Não há construtor de cópia público no
    /// domínio — reflexão é usada apenas aqui, em código de teste, para
    /// simular materializações independentes equivalentes a consultas
    /// distintas ao banco real.
    /// </summary>
    private static Triagem DeepClone(Triagem source)
    {
        var clone = (Triagem)Activator.CreateInstance(typeof(Triagem), nonPublic: true)!;

        foreach (var field in typeof(Triagem).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            var value = field.GetValue(source);
            if (value is Dictionary<ETriagemChecklistItem, int> dictionary)
            {
                value = new Dictionary<ETriagemChecklistItem, int>(dictionary);
            }

            field.SetValue(clone, value);
        }

        return clone;
    }
}
