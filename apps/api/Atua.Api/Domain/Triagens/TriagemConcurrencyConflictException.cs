namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Sinaliza que a persistência da transição de estado da triagem perdeu a
/// arbitragem de concorrência otimista (<see cref="Triagem.ConcurrencyToken"/>)
/// — outro processo já persistiu uma transição concorrente para a mesma
/// triagem entre a leitura e a tentativa de gravação desta chamada.
///
/// O chamador nunca deve repetir o efeito externo (chamada ao canal) ao
/// tratar este conflito: deve apenas recarregar a instância vencedora (ex.:
/// via <c>ITriagemRepository.FindByKeyAsync</c>) e adotar o resultado já
/// persistido — ver <c>StartTriagemUseCase</c>.
/// </summary>
public sealed class TriagemConcurrencyConflictException : Exception
{
    public TriagemConcurrencyConflictException(Guid triagemId, Exception innerException)
        : base($"Conflito de concorrência otimista ao persistir a transição da triagem '{triagemId}'.",
            innerException)
    {
        TriagemId = triagemId;
    }

    /// <summary>Triagem cuja persistência de transição colidiu com uma escrita concorrente.</summary>
    public Guid TriagemId { get; }
}
