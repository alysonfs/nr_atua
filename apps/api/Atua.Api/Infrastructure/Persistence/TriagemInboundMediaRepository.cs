using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Persiste metadados de mídia inbound da triagem e orquestra a gravação
/// condicional do binário em <see cref="ITriagemInboundMediaStorage"/>
/// (ADR-046). Segue o mesmo padrão transacional de
/// <see cref="TriagemInboundMessageRepository"/> (ADR-043): dedup por
/// <c>(TenantId, ChannelIdempotencyKey)</c>, isolamento <c>Serializable</c>,
/// deduplicar + (quando aplicável) transicionar é atômico.
///
/// Diferença deliberada em relação ao texto: a resolução da triagem
/// candidata não se restringe a <c>ContatoIniciado</c>/<c>EmColeta</c> — o
/// PO decidiu (2026-10-21) armazenar a mídia mesmo com a triagem em
/// qualquer outro status, desde que haja exatamente uma correspondência
/// pelo telefone. Somente <c>ContatoIniciado</c> produz a transição para
/// <c>EmColeta</c>; os demais apenas armazenam (<see cref="ETriagemInboundMediaOutcome.Armazenada"/>).
/// </summary>
public sealed class TriagemInboundMediaRepository(
    AtuaDbContext dbContext,
    ITriagemInboundMediaStorage storage,
    TimeProvider timeProvider) : ITriagemInboundMediaRepository
{
    public async Task<TriagemInboundMediaExistingRecord?> FindByIdempotencyKeyAsync(
        Guid tenantId, string channelIdempotencyKey, CancellationToken cancellationToken)
    {
        var existing = await dbContext.TriagemInboundMedia.AsNoTracking()
            .SingleOrDefaultAsync(media =>
                media.TenantId == tenantId && media.ChannelIdempotencyKey == channelIdempotencyKey,
                cancellationToken);

        return existing is null ? null : ToExistingRecord(existing);
    }

    public async Task<TriagemInboundMediaResult> PersistTechnicalOutcomeAsync(
        TriagemInboundMediaCommand command,
        ETriagemInboundMediaOutcome outcome,
        string? sha256,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        var existing = await dbContext.TriagemInboundMedia.AsNoTracking()
            .SingleOrDefaultAsync(media =>
                media.TenantId == command.TenantId &&
                media.ChannelIdempotencyKey == command.ChannelIdempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToResult(existing);
        }

        var media = BuildMedia(command, outcome, sha256, triagemId: null, candidateIds: [], storageKey: null);

        try
        {
            dbContext.TriagemInboundMedia.Add(media);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResult(media);
        }
        catch (DbUpdateException exception) when (IsIdempotencyUniqueViolation(exception))
        {
            return await RecoverFromRaceAsync(command, transaction, exception, cancellationToken);
        }
    }

    public async Task<TriagemInboundMediaResult> ProcessAcceptedMediaAsync(
        TriagemInboundMediaCommand command,
        string sha256,
        byte[] content,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        var existing = await dbContext.TriagemInboundMedia.AsNoTracking()
            .SingleOrDefaultAsync(media =>
                media.TenantId == command.TenantId &&
                media.ChannelIdempotencyKey == command.ChannelIdempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToResult(existing);
        }

        var senderKey = TriagemInboundMessageRepository.ToPhoneMatchKey(command.SenderPhone);
        // Decisão do PO (2026-10-21): ao contrário do texto (ADR-043), a mídia é
        // associada a uma triagem única independentemente do status — não se
        // restringe a ContatoIniciado/EmColeta.
        var possibleMatches = await (
            from triagem in dbContext.Triagens
            join channelCommand in dbContext.TriagemChannelCommands
                on triagem.Id equals channelCommand.TriagemId
            where triagem.TenantId == command.TenantId && triagem.IntegrationId == command.IntegrationId
            select new { Triagem = triagem, channelCommand.RecipientPhone }
        ).ToListAsync(cancellationToken);

        var matchingTriagens = possibleMatches
            .Where(candidate => candidate.RecipientPhone is not null
                && TriagemInboundMessageRepository.ToPhoneMatchKey(candidate.RecipientPhone) == senderKey)
            .Select(candidate => candidate.Triagem)
            .DistinctBy(triagem => triagem.Id)
            .ToArray();

        var candidateIds = matchingTriagens.Select(triagem => triagem.Id).ToArray();
        ETriagemInboundMediaOutcome outcome;
        Triagem? matchedTriagem = null;
        string? storageKey = null;
        var mediaId = Guid.CreateVersion7();

        if (matchingTriagens.Length == 0)
        {
            outcome = ETriagemInboundMediaOutcome.SemTriagem;
        }
        else if (matchingTriagens.Length > 1)
        {
            outcome = ETriagemInboundMediaOutcome.Ambigua;
        }
        else
        {
            matchedTriagem = matchingTriagens[0];
            outcome = matchedTriagem.Status == ETriagemStatus.ContatoIniciado
                ? ETriagemInboundMediaOutcome.ColetaIniciada
                : ETriagemInboundMediaOutcome.Armazenada;

            var key = new TriagemInboundMediaStorageKey(command.TenantId, mediaId);
            await using (var stream = new MemoryStream(content, writable: false))
            {
                await storage.PutIfAbsentAsync(key, stream, command.ContentType, content.LongLength, cancellationToken);
            }
            storageKey = key.Value;

            if (outcome == ETriagemInboundMediaOutcome.ColetaIniciada)
            {
                matchedTriagem.IniciarColeta(command.ReceivedAtUtc);
            }
        }

        var media = BuildMedia(
            command, outcome, sha256, matchedTriagem?.Id,
            outcome == ETriagemInboundMediaOutcome.Ambigua ? candidateIds : [],
            storageKey, mediaId);

        try
        {
            dbContext.TriagemInboundMedia.Add(media);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResult(media);
        }
        catch (DbUpdateException exception) when (IsIdempotencyUniqueViolation(exception))
        {
            return await RecoverFromRaceAsync(command, transaction, exception, cancellationToken);
        }
    }

    private async Task<TriagemInboundMediaResult> RecoverFromRaceAsync(
        TriagemInboundMediaCommand command,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        DbUpdateException exception,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.TriagemInboundMedia.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.TenantId == command.TenantId &&
                item.ChannelIdempotencyKey == command.ChannelIdempotencyKey,
                cancellationToken);
        if (persisted is null)
        {
            throw new InvalidOperationException(
                "A chave idempotente da mídia recebida colidiu, mas o evento persistido não foi encontrado.",
                exception);
        }

        return ToResult(persisted);
    }

    private TriagemInboundMedia BuildMedia(
        TriagemInboundMediaCommand command,
        ETriagemInboundMediaOutcome outcome,
        string? sha256,
        Guid? triagemId,
        IReadOnlyCollection<Guid> candidateIds,
        string? storageKey,
        Guid? id = null) =>
        new(
            id ?? Guid.CreateVersion7(),
            command.TenantId,
            command.IntegrationId,
            command.ChannelMessageId,
            command.ChannelIdempotencyKey,
            command.CorrelationId,
            TriagemInboundMessageRepository.NormalizePhone(command.SenderPhone),
            command.ConversationKind,
            command.FromMe,
            command.ReceivedAtUtc,
            command.MediaKind,
            command.ContentType,
            command.SizeBytes,
            sha256,
            command.IsVoiceNote,
            command.DurationSeconds,
            command.Caption,
            outcome,
            triagemId,
            candidateIds,
            storageKey,
            timeProvider.GetUtcNow());

    private static TriagemInboundMediaExistingRecord ToExistingRecord(TriagemInboundMedia media) =>
        new(
            media.ChannelMessageId,
            media.Outcome,
            media.TriagemId,
            media.TriagemId is null ? media.CandidateTriagemIds.Length : 1,
            media.ContentType,
            media.SizeBytes,
            media.Sha256,
            media.Id);

    private static TriagemInboundMediaResult ToResult(TriagemInboundMedia media) =>
        new(
            media.ChannelMessageId,
            media.Outcome,
            media.TriagemId,
            media.TriagemId is null ? media.CandidateTriagemIds.Length : 1,
            media.Id);

    private static bool IsIdempotencyUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && postgresException.ConstraintName == TriagemInboundMediaPersistenceSchema.IdempotencyUniqueIndexName;
}
