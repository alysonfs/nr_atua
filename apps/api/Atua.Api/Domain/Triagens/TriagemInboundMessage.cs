namespace Atua.Api.Domain.Triagens;

public sealed class TriagemInboundMessage
{
    private TriagemInboundMessage() { }

    public TriagemInboundMessage(
        Guid id,
        Guid tenantId,
        Guid integrationId,
        string channelMessageId,
        string channelIdempotencyKey,
        Guid correlationId,
        string senderPhone,
        string conversationKind,
        bool fromMe,
        DateTimeOffset receivedAtUtc,
        string kind,
        string? text,
        ETriagemInboundMessageOutcome outcome,
        Guid? triagemId,
        IReadOnlyCollection<Guid> candidateTriagemIds,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        ChannelMessageId = channelMessageId;
        ChannelIdempotencyKey = channelIdempotencyKey;
        CorrelationId = correlationId;
        SenderPhone = senderPhone;
        ConversationKind = conversationKind;
        FromMe = fromMe;
        ReceivedAtUtc = receivedAtUtc;
        Kind = kind;
        Text = text;
        Outcome = outcome;
        TriagemId = triagemId;
        CandidateTriagemIds = candidateTriagemIds.ToArray();
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid IntegrationId { get; private set; }
    public string ChannelMessageId { get; private set; } = null!;
    public string ChannelIdempotencyKey { get; private set; } = null!;
    public Guid CorrelationId { get; private set; }
    public string SenderPhone { get; private set; } = null!;
    public string ConversationKind { get; private set; } = null!;
    public bool FromMe { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public string Kind { get; private set; } = null!;
    public string? Text { get; private set; }
    public ETriagemInboundMessageOutcome Outcome { get; private set; }
    public Guid? TriagemId { get; private set; }
    public Guid[] CandidateTriagemIds { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
