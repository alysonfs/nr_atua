namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Sinaliza que o canal (<c>ITriagemChannel</c>) reportou falha transitória
/// ao emitir o comando de envio do template inicial. A triagem permanece em
/// <see cref="ETriagemStatus.AguardandoEnvio"/>, com a mesma chave de
/// idempotência preservada (ADR-039, item 7: "falha transitória preserva a
/// mesma chave durante a retentativa"). Classificada pela outbox
/// (<c>TriagemOutboxDrainService</c>) como falha elegível a retentativa com
/// backoff (ADR-041) — nunca mascarada como sucesso nem como falha
/// definitiva.
/// </summary>
public sealed class TriagemChannelTransientFailureException : Exception
{
    public TriagemChannelTransientFailureException(Guid triagemId, string? channelErrorCode,
        string? channelErrorDescription)
        : base(BuildMessage(triagemId, channelErrorCode))
    {
        TriagemId = triagemId;
        ChannelErrorCode = channelErrorCode;
        ChannelErrorDescription = channelErrorDescription;
    }

    /// <summary>Triagem para a qual o envio do template falhou transitoriamente.</summary>
    public Guid TriagemId { get; }

    /// <summary>Código seguro de erro reportado pelo canal — nunca texto livre sensível.</summary>
    public string? ChannelErrorCode { get; }

    /// <summary>Descrição segura do erro reportado pelo canal.</summary>
    public string? ChannelErrorDescription { get; }

    private static string BuildMessage(Guid triagemId, string? channelErrorCode) =>
        channelErrorCode is null
            ? $"Falha transitória do canal ao enviar o template inicial da triagem '{triagemId}'."
            : $"Falha transitória do canal ao enviar o template inicial da triagem '{triagemId}' " +
              $"(código '{channelErrorCode}').";
}
