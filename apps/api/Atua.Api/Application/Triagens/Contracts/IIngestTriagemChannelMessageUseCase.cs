namespace Atua.Api.Application.Triagens.Contracts;

public interface IIngestTriagemChannelMessageUseCase
{
    Task<TriagemInboundMessageResult> ExecuteAsync(
        TriagemInboundMessageCommand command,
        CancellationToken cancellationToken);
}
