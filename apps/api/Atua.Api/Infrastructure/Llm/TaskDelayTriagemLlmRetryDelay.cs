using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>Implementação de produção de <see cref="ITriagemLlmRetryDelay"/> — espera real via <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
public sealed class TaskDelayTriagemLlmRetryDelay : ITriagemLlmRetryDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
