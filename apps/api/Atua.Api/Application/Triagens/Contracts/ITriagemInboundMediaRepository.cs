using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

public interface ITriagemInboundMediaRepository
{
    /// <summary>
    /// Busca barata (sem transação) usada apenas para a reentrega idempotente
    /// — retorna os metadados declarados já persistidos, sem acessar o
    /// binário.
    /// </summary>
    Task<TriagemInboundMediaExistingRecord?> FindByIdempotencyKeyAsync(
        Guid tenantId,
        string channelIdempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persiste um evento de mídia tecnicamente encerrado sem armazenar
    /// binário — grupo, mensagem própria, tipo rejeitado ou tamanho
    /// rejeitado (ADR-043/decisão de mídia). Idempotente por
    /// <c>(TenantId, ChannelIdempotencyKey)</c>.
    /// </summary>
    Task<TriagemInboundMediaResult> PersistTechnicalOutcomeAsync(
        TriagemInboundMediaCommand command,
        ETriagemInboundMediaOutcome outcome,
        string? sha256,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolve a triagem elegível (qualquer status, premissa do produto) pelo
    /// telefone do remetente e persiste o evento. Quando há exatamente uma
    /// triagem candidata, grava o binário em S3 (<see cref="ITriagemInboundMediaStorage"/>)
    /// antes de confirmar a transação e, quando a triagem está em
    /// <c>ContatoIniciado</c>, inicia a coleta — mesma fronteira do ADR-043,
    /// aplicada à mídia. Quando não há triagem única (nenhuma ou ambígua), o
    /// binário nunca é gravado. Idempotente por
    /// <c>(TenantId, ChannelIdempotencyKey)</c>.
    /// </summary>
    Task<TriagemInboundMediaResult> ProcessAcceptedMediaAsync(
        TriagemInboundMediaCommand command,
        string sha256,
        byte[] content,
        CancellationToken cancellationToken);
}

public sealed record TriagemInboundMediaCommand(
    Guid TenantId,
    Guid IntegrationId,
    string ChannelMessageId,
    string ChannelIdempotencyKey,
    Guid CorrelationId,
    string SenderPhone,
    string ConversationKind,
    bool FromMe,
    DateTimeOffset ReceivedAtUtc,
    ETriagemInboundMediaKind MediaKind,
    string ContentType,
    long SizeBytes,
    bool IsVoiceNote,
    int? DurationSeconds,
    string? Caption);

/// <summary>Metadados declarados já persistidos para uma chave idempotente — usados apenas para detectar conflito na reentrega.</summary>
public sealed record TriagemInboundMediaExistingRecord(
    string ChannelMessageId,
    ETriagemInboundMediaOutcome Outcome,
    Guid? TriagemId,
    int CandidateCount,
    string ContentType,
    long SizeBytes,
    string? Sha256,
    Guid MediaId);

public sealed record TriagemInboundMediaResult(
    string ChannelMessageId,
    ETriagemInboundMediaOutcome Outcome,
    Guid? TriagemId,
    int CandidateCount,
    Guid MediaId);

/// <summary>Mesma chave idempotente já associada a conteúdo diferente — nunca ocorre com retentativa legítima do Channel.</summary>
public sealed class TriagemInboundMediaIdempotencyConflictException(string channelIdempotencyKey)
    : Exception($"A chave idempotente '{channelIdempotencyKey}' já está associada a uma mídia com " +
        "contentType/sizeBytes diferentes do evento recebido.");
