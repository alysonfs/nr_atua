namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Metadados de uma mídia recebida pelo canal WhatsApp (incremento de mídia
/// sobre o ADR-043). O binário em si nunca é persistido aqui — apenas em
/// S3, sob a chave determinística derivada de <see cref="TenantId"/> e
/// <see cref="Id"/> (ver <c>ITriagemInboundMediaStorage</c>), somente quando
/// <see cref="Outcome"/> é <see cref="ETriagemInboundMediaOutcome.ColetaIniciada"/>
/// ou <see cref="ETriagemInboundMediaOutcome.Armazenada"/>.
///
/// Retenção: segue a mesma política dos ADR-045/046 — cinco anos desde
/// <see cref="ReceivedAtUtc"/>, elegibilidade de expurgo físico um ano
/// depois. O expurgo é executado pelo
/// <see cref="Atua.Api.Application.Triagens.TriagemInboundMediaPurgeService"/> e
/// hospedado por <c>TriagemInboundMediaPurgeJob</c>.
/// </summary>
public sealed class TriagemInboundMedia
{
    private TriagemInboundMedia() { }

    public TriagemInboundMedia(
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
        ETriagemInboundMediaKind mediaKind,
        string contentType,
        long sizeBytes,
        string? sha256,
        bool isVoiceNote,
        int? durationSeconds,
        string? caption,
        ETriagemInboundMediaOutcome outcome,
        Guid? triagemId,
        IReadOnlyCollection<Guid> candidateTriagemIds,
        string? storageKey,
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
        ExpiresAtUtc = TriagemInboundMessageRetentionPolicy.CalculateExpirationUtc(receivedAtUtc);
        MediaKind = mediaKind;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        IsVoiceNote = isVoiceNote;
        DurationSeconds = durationSeconds;
        Caption = caption;
        Outcome = outcome;
        TriagemId = triagemId;
        CandidateTriagemIds = candidateTriagemIds.ToArray();
        StorageKey = storageKey;
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
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public ETriagemInboundMediaKind MediaKind { get; private set; }
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string? Sha256 { get; private set; }
    public bool IsVoiceNote { get; private set; }
    public int? DurationSeconds { get; private set; }
    public string? Caption { get; private set; }
    public ETriagemInboundMediaOutcome Outcome { get; private set; }
    public Guid? TriagemId { get; private set; }
    public Guid[] CandidateTriagemIds { get; private set; } = [];

    /// <summary>
    /// Chave do objeto em S3 (<c>tenants/{TenantId}/inbound-media/{Id}</c>),
    /// presente apenas quando o binário foi efetivamente armazenado.
    /// </summary>
    public string? StorageKey { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
