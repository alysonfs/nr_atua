using System.Collections.Concurrent;
using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Canal fake in-process de <see cref="ITriagemChannel"/>. NÃO representa
/// integração real com WhatsApp/Baileys nem qualquer fornecedor — é um
/// substituto explícito enquanto o canal real não é implementado (mesmo
/// princípio de <c>FakeIServiceAuthClient</c>), suficiente para exercitar o
/// fluxo ponta-a-ponta do comando <c>SendTemplate</c> (arquitetura, seção
/// 5.3) sem dependência externa.
///
/// TODO: substituir por uma implementação real do canal quando o fornecedor
/// (Baileys ou equivalente) e o texto do template estiverem aprovados pelo
/// PO (arquitetura, seção 3).
///
/// Deduplica por <see cref="TriagemSendTemplateCommand.IdempotencyKey"/>
/// mesmo sob concorrência: usa <see cref="Lazy{T}"/> com
/// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> dentro de um
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>, garantindo que o corpo
/// que produz um resultado terminal execute exatamente uma vez por chave,
/// mesmo quando múltiplas chamadas concorrentes chegam com a mesma chave
/// (ADR-039, item 3). Falhas transitórias não são memorizadas: uma nova
/// tentativa com a mesma chave pode consultar o canal novamente. Registrado
/// como singleton — o estado de deduplicação é apenas deste fake e não
/// substitui a deduplicação durável exigida da futura implementação real.
/// </summary>
public sealed class FakeTriagemChannel : ITriagemChannel
{
    private readonly ConcurrentDictionary<string, Lazy<TriagemChannelSendResult>> _results = new();
    private readonly ConcurrentDictionary<string, Func<TriagemSendTemplateCommand, TriagemChannelSendResult>>
        _configuredResults = new();

    private int _realSendCount;

    /// <summary>
    /// Quantidade de execuções reais de envio (uma por
    /// <see cref="TriagemSendTemplateCommand.IdempotencyKey"/> distinta,
    /// independentemente de quantas chamadas concorrentes/repetidas chegaram
    /// com a mesma chave) — usado por testes para provar a deduplicação.
    /// </summary>
    public int RealSendCount => Volatile.Read(ref _realSendCount);

    /// <summary>
    /// Configura o resultado da próxima execução real para
    /// <paramref name="idempotencyKey"/>. Sem configuração, o resultado
    /// padrão é sucesso. Resultados terminais são memorizados; falhas
    /// transitórias liberam a chave para uma nova tentativa.
    /// </summary>
    public void ConfigureResult(string idempotencyKey, TriagemChannelSendResult result) =>
        _configuredResults[idempotencyKey] = _ => result;

    public Task<TriagemChannelSendResult> SendTemplateAsync(TriagemSendTemplateCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lazy = _results.GetOrAdd(command.IdempotencyKey, key => new Lazy<TriagemChannelSendResult>(
            () =>
            {
                Interlocked.Increment(ref _realSendCount);
                var resolver = _configuredResults.TryGetValue(key, out var configured)
                    ? configured
                    : DefaultSuccess;
                return resolver(command);
            },
            LazyThreadSafetyMode.ExecutionAndPublication));

        var result = lazy.Value;
        if (result.Outcome == ETriagemChannelSendOutcome.TransientFailure)
        {
            RemoveIfCurrent(command.IdempotencyKey, lazy);
        }

        return Task.FromResult(result);
    }

    private void RemoveIfCurrent(string key, Lazy<TriagemChannelSendResult> value) =>
        ((ICollection<KeyValuePair<string, Lazy<TriagemChannelSendResult>>>)_results)
        .Remove(new KeyValuePair<string, Lazy<TriagemChannelSendResult>>(key, value));

    private static TriagemChannelSendResult DefaultSuccess(TriagemSendTemplateCommand command) =>
        TriagemChannelSendResult.Success($"fake-{command.IdempotencyKey}");
}
