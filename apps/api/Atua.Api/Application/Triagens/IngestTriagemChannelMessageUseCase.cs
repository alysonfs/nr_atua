using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.Extensions.Logging;

namespace Atua.Api.Application.Triagens;

public sealed class IngestTriagemChannelMessageUseCase(
    ITriagemInboundMessageRepository repository,
    ILogger<IngestTriagemChannelMessageUseCase> logger) : IIngestTriagemChannelMessageUseCase
{
    public async Task<TriagemInboundMessageResult> ExecuteAsync(
        TriagemInboundMessageCommand command,
        CancellationToken cancellationToken)
    {
        var result = await repository.ProcessAsync(command, cancellationToken);

        if (result.Outcome == ETriagemInboundMessageOutcome.Ambigua)
        {
            logger.LogWarning(
                "[TRIAGEM-CHANNEL] Mensagem recebida ambígua; aguardando resolução manual. " +
                "CorrelationId={CorrelationId} CandidateCount={CandidateCount}",
                command.CorrelationId,
                result.CandidateCount);
        }
        else
        {
            logger.LogInformation(
                "[TRIAGEM-CHANNEL] Mensagem recebida processada. CorrelationId={CorrelationId} Outcome={Outcome}",
                command.CorrelationId,
                result.Outcome);
        }

        return result;
    }
}
