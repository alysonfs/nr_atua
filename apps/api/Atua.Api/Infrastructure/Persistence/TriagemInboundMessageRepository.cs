using System.Text.RegularExpressions;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atua.Api.Infrastructure.Persistence;

public sealed partial class TriagemInboundMessageRepository(AtuaDbContext dbContext, TimeProvider timeProvider)
    : ITriagemInboundMessageRepository
{
    public async Task<TriagemInboundMessageResult> ProcessAsync(
        TriagemInboundMessageCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        var existing = await dbContext.TriagemInboundMessages.AsNoTracking()
            .SingleOrDefaultAsync(message =>
                message.TenantId == command.TenantId &&
                message.ChannelIdempotencyKey == command.ChannelIdempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToResult(existing);
        }

        var outcome = GetIgnoredOutcome(command);
        Triagem? matchedTriagem = null;
        Guid[] candidateIds = [];

        if (outcome is null)
        {
            var senderKey = ToPhoneMatchKey(command.SenderPhone);
            // A resolução da triagem candidata não se restringe a
            // ContatoIniciado/EmColeta (ADR-043 deixou explicitamente para
            // quando "o pool e o painel do operador consumirem este mesmo
            // evento" — RF-TRIAGEM-003/ETriagemStatus.PoolStatuses, este
            // incremento). Mesmo padrão já adotado para mídia (decisão do PO
            // em 2026-10-21, ver TriagemInboundMediaRepository): qualquer
            // status é elegível para vínculo, sem reabrir conversa — a LLM
            // nunca é acionada aqui, apenas IniciarColeta quando aplicável.
            var possibleMatches = await (
                from triagem in dbContext.Triagens
                join channelCommand in dbContext.TriagemChannelCommands
                    on triagem.Id equals channelCommand.TriagemId
                where triagem.TenantId == command.TenantId
                    && triagem.IntegrationId == command.IntegrationId
                select new { Triagem = triagem, channelCommand.RecipientPhone }
            ).ToListAsync(cancellationToken);

            var matchingTriagens = possibleMatches
                .Where(candidate => candidate.RecipientPhone is not null
                    && ToPhoneMatchKey(candidate.RecipientPhone) == senderKey)
                .Select(candidate => candidate.Triagem)
                .DistinctBy(triagem => triagem.Id)
                .ToArray();

            candidateIds = matchingTriagens.Select(triagem => triagem.Id).ToArray();
            if (matchingTriagens.Length == 0)
            {
                outcome = ETriagemInboundMessageOutcome.SemTriagem;
            }
            else if (matchingTriagens.Length > 1)
            {
                outcome = ETriagemInboundMessageOutcome.Ambigua;
            }
            else
            {
                matchedTriagem = matchingTriagens[0];
                if (matchedTriagem.Status == ETriagemStatus.ContatoIniciado)
                {
                    matchedTriagem.IniciarColeta(command.ReceivedAtUtc);
                    outcome = ETriagemInboundMessageOutcome.ColetaIniciada;
                }
                else
                {
                    outcome = ETriagemInboundMessageOutcome.JaEmColeta;
                }
            }
        }

        var message = new TriagemInboundMessage(
            Guid.CreateVersion7(),
            command.TenantId,
            command.IntegrationId,
            command.ChannelMessageId,
            command.ChannelIdempotencyKey,
            command.CorrelationId,
            NormalizePhone(command.SenderPhone),
            command.ConversationKind,
            command.FromMe,
            command.ReceivedAtUtc,
            command.Kind,
            command.Text,
            outcome!.Value,
            matchedTriagem?.Id,
            outcome == ETriagemInboundMessageOutcome.Ambigua ? candidateIds : [],
            timeProvider.GetUtcNow());

        dbContext.TriagemInboundMessages.Add(message);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResult(message);
        }
        catch (DbUpdateException exception) when (IsIdempotencyUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var persisted = await dbContext.TriagemInboundMessages.AsNoTracking()
                .SingleOrDefaultAsync(item =>
                    item.TenantId == command.TenantId &&
                    item.ChannelIdempotencyKey == command.ChannelIdempotencyKey,
                    cancellationToken);
            if (persisted is null)
            {
                throw new InvalidOperationException(
                    "A chave idempotente da mensagem recebida colidiu, mas o evento persistido não foi encontrado.",
                    exception);
            }

            return ToResult(persisted);
        }
    }

    private static ETriagemInboundMessageOutcome? GetIgnoredOutcome(TriagemInboundMessageCommand command)
    {
        if (string.Equals(command.ConversationKind, "group", StringComparison.OrdinalIgnoreCase))
        {
            return ETriagemInboundMessageOutcome.IgnoradaGrupo;
        }

        if (command.FromMe)
        {
            return ETriagemInboundMessageOutcome.IgnoradaPropria;
        }

        return !string.Equals(command.Kind, "text", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(command.Text)
                ? ETriagemInboundMessageOutcome.IgnoradaVazia
                : null;
    }

    internal static string NormalizePhone(string phone) => NonDigitsRegex().Replace(phone, "");

    // WhatsApp often identifies Brazilian mobiles without the ninth digit (55 + DDD + 8 digits),
    // so both forms must match the phone registered on the work order.
    internal static string ToPhoneMatchKey(string phone)
    {
        var digits = NormalizePhone(phone);
        return digits.Length == 13 && digits.StartsWith("55", StringComparison.Ordinal) && digits[4] == '9'
            ? digits.Remove(4, 1)
            : digits;
    }

    private static TriagemInboundMessageResult ToResult(TriagemInboundMessage message) =>
        new(
            message.ChannelMessageId,
            message.Outcome,
            message.TriagemId,
            message.TriagemId is null ? message.CandidateTriagemIds.Length : 1,
            message.Id);

    private static bool IsIdempotencyUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && postgresException.ConstraintName == TriagemInboundMessagePersistenceSchema.IdempotencyUniqueIndexName;

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigitsRegex();
}
