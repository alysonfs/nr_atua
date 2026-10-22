using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Um operador do posto assume uma triagem do pool com exclusividade (épico
/// de pool de triagem, "C3 — Assumir"). Concorrência otimista via
/// <see cref="Triagem.ConcurrencyToken"/> (<see cref="ITriagemRepository"/>):
/// o primeiro a persistir vence; o perdedor recebe
/// <see cref="EAssumeTriagemOutcome.ConcurrencyConflict"/> com quem assumiu e
/// quando (critério de aceite 7).
/// </summary>
public interface IAssumeTriagemUseCase
{
    Task<AssumeTriagemResult> ExecuteAsync(Guid tenantId, Guid triagemId, Guid requestingUserId,
        CancellationToken cancellationToken);
}

public enum EAssumeTriagemOutcome
{
    Success,

    /// <summary>Usuário autenticado não é membro do tenant.</summary>
    Forbidden,

    /// <summary>Triagem inexistente neste tenant.</summary>
    NotFound,

    /// <summary>
    /// A triagem não está em nenhum dos quatro estados do pool
    /// (<see cref="Triagem.PoolStatuses"/>) no momento da tentativa — ex.:
    /// já assumida por outro operador, ou ainda em coleta.
    /// </summary>
    NotInPool,

    /// <summary>
    /// Outro processo persistiu uma mutação concorrente da mesma triagem
    /// entre a leitura e a tentativa de gravação desta chamada (ex.: outro
    /// operador venceu a corrida de assunção por uma fração de segundo). O
    /// chamador deve reconsultar o estado atual — <see cref="AssumeTriagemResult.CurrentAssumidaPorUserId"/>
    /// e <see cref="AssumeTriagemResult.CurrentAssumidaEmUtc"/> trazem quem
    /// assumiu e quando, quando disponível.
    /// </summary>
    ConcurrencyConflict,
}

public sealed record AssumeTriagemResult(
    EAssumeTriagemOutcome Outcome,
    Guid? CurrentAssumidaPorUserId = null,
    DateTimeOffset? CurrentAssumidaEmUtc = null,
    ETriagemStatus? CurrentStatus = null)
{
    public static AssumeTriagemResult Failure(EAssumeTriagemOutcome outcome) => new(outcome);
}
