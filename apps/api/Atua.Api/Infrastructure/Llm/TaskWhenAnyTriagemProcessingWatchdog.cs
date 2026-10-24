using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Implementação de produção de <see cref="ITriagemProcessingWatchdog"/> —
/// corrida real via <see cref="Task.WhenAny(Task[])"/> entre o
/// processamento do turno e <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
/// </summary>
public sealed class TaskWhenAnyTriagemProcessingWatchdog : ITriagemProcessingWatchdog
{
    public async Task<bool> HasElapsedBeforeCompletionAsync(Task processing, TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var delayTask = Task.Delay(delay, cancellationToken);
        var completed = await Task.WhenAny(processing, delayTask);
        return completed == delayTask;
    }
}
