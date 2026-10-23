using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

public interface ITriagemInboundMessageRepository
{
    Task<TriagemInboundMessageResult> ProcessAsync(
        TriagemInboundMessageCommand command,
        CancellationToken cancellationToken);
}

public sealed record TriagemInboundMessageCommand(
    Guid TenantId,
    Guid IntegrationId,
    string ChannelMessageId,
    string ChannelIdempotencyKey,
    Guid CorrelationId,
    string SenderPhone,
    string ConversationKind,
    bool FromMe,
    DateTimeOffset ReceivedAtUtc,
    string Kind,
    string? Text);

public sealed record TriagemInboundMessageResult(
    string ChannelMessageId,
    ETriagemInboundMessageOutcome Outcome,
    Guid? TriagemId,
    int CandidateCount,
    Guid? MessageId = null);
