using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Contrato de claim em lote da outbox do gatilho de triagem (ADR-041). A
/// implementação concreta deve usar <c>FOR UPDATE SKIP LOCKED</c> + lease no
/// PostgreSQL — nunca um lock em memória — para que múltiplas instâncias da
/// Master API possam drenar concorrentemente sem reivindicar a mesma linha.
/// </summary>
public interface ITriagemOutboxRepository
{
    /// <summary>
    /// Reivindica, atomicamente, até <paramref name="batchSize"/> linhas
    /// <c>Pending</c> vencidas ou <c>Processing</c> com lease expirada,
    /// marcando-as <c>Processing</c> com uma nova lease de
    /// <paramref name="leaseDuration"/> a partir de <paramref name="now"/>.
    /// As entidades retornadas já estão rastreadas pelo contexto de
    /// persistência — alterá-las e chamar <see cref="SaveChangesAsync"/>
    /// persiste o resultado do processamento.
    /// </summary>
    Task<IReadOnlyList<TriagemTriggerOutbox>> ClaimBatchAsync(int batchSize, TimeSpan leaseDuration,
        DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Persiste as alterações feitas nas entidades retornadas por <see cref="ClaimBatchAsync"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
