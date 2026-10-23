namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Abstrai a espera entre tentativas de retry a <see cref="ITriagemLlmClient"/>
/// quando o fornecedor está indisponível (decisão do PO, 2026-10-23: até 2
/// novas tentativas com backoff exponencial configurável antes de mover a
/// triagem para <see cref="Domain.Triagens.ETriagemStatus.AguardandoIntervencao"/>).
/// Existe como interface (em vez de <c>Task.Delay</c> direto) só para que
/// testes não precisem esperar segundos reais — a implementação de produção
/// é a única que efetivamente aguarda.
/// </summary>
public interface ITriagemLlmRetryDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}
