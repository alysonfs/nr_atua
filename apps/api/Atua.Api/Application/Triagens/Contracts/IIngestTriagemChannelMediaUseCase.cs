namespace Atua.Api.Application.Triagens.Contracts;

public interface IIngestTriagemChannelMediaUseCase
{
    Task<TriagemInboundMediaUseCaseResult> ExecuteAsync(
        TriagemInboundMediaCommand command,
        Stream fileContent,
        CancellationToken cancellationToken);
}

public sealed record TriagemInboundMediaUseCaseResult(
    TriagemInboundMediaResult? Result,
    bool IsIdempotencyConflict);
