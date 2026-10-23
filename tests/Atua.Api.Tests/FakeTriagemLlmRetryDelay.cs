using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Tests;

/// <summary>
/// Fake de <see cref="ITriagemLlmRetryDelay"/> — não espera de verdade
/// (testes não devem levar segundos reais), mas registra cada atraso
/// solicitado para que os testes verifiquem o backoff exponencial
/// (decisão do PO, 2026-10-23: 1s e 2s, total 3 chamadas).
/// </summary>
internal sealed class FakeTriagemLlmRetryDelay : ITriagemLlmRetryDelay
{
    private readonly List<TimeSpan> _requestedDelays = new();

    public IReadOnlyList<TimeSpan> RequestedDelays => _requestedDelays;

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requestedDelays.Add(delay);
        return Task.CompletedTask;
    }
}
