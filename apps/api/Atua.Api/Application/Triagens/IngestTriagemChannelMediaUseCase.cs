using System.Security.Cryptography;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.Extensions.Logging;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Orquestra a ingestão de mídia inbound da triagem: reentrega idempotente,
/// filtros de grupo/mensagem própria, validação técnica de tipo/tamanho e,
/// somente então, delega ao repositório a resolução da triagem e o
/// armazenamento condicional do binário. Mídia é um evento único — nunca
/// também contabilizada como <c>MessageReceived</c> (ADR-043).
/// </summary>
public sealed class IngestTriagemChannelMediaUseCase(
    ITriagemInboundMediaRepository repository,
    TriagemLlmConversationService llmConversationService,
    ILogger<IngestTriagemChannelMediaUseCase> logger) : IIngestTriagemChannelMediaUseCase
{
    // Decisão do PO (2026-10-21): limite máximo de 16 MiB por arquivo.
    public const long MaxContentLengthBytes = 16 * 1024 * 1024;

    public async Task<TriagemInboundMediaUseCaseResult> ExecuteAsync(
        TriagemInboundMediaCommand command,
        Stream fileContent,
        CancellationToken cancellationToken)
    {
        var existing = await repository.FindByIdempotencyKeyAsync(
            command.TenantId, command.ChannelIdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var (existingContent, existingSha256, existingContentExceeded) =
                await ReadCappedAsync(fileContent, MaxContentLengthBytes, cancellationToken);
            if (!string.Equals(existing.ContentType, command.ContentType, StringComparison.OrdinalIgnoreCase)
                || existing.SizeBytes != command.SizeBytes
                || existingContentExceeded
                || existingContent.LongLength != command.SizeBytes
                || !string.Equals(existing.Sha256, existingSha256, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "[TRIAGEM-CHANNEL-MEDIA] Conflito de idempotência: a mesma chave chegou com " +
                    "conteúdo diferente. CorrelationId={CorrelationId}",
                    command.CorrelationId);
                return new TriagemInboundMediaUseCaseResult(null, IsIdempotencyConflict: true);
            }

            var existingResult = new TriagemInboundMediaResult(
                existing.ChannelMessageId, existing.Outcome, existing.TriagemId, existing.CandidateCount,
                existing.MediaId);
            await ConductConversationAsync(command, existingResult, cancellationToken);
            return new TriagemInboundMediaUseCaseResult(existingResult, IsIdempotencyConflict: false);
        }

        var ignoredOutcome = GetIgnoredOutcome(command);
        if (ignoredOutcome is not null)
        {
            var (_, ignoredSha256, _) =
                await ReadCappedAsync(fileContent, MaxContentLengthBytes, cancellationToken);
            var ignoredResult = await repository.PersistTechnicalOutcomeAsync(
                command, ignoredOutcome.Value, ignoredSha256, cancellationToken);
            return new TriagemInboundMediaUseCaseResult(ignoredResult, IsIdempotencyConflict: false);
        }

        var (content, sha256, exceeded) = await ReadCappedAsync(fileContent, MaxContentLengthBytes, cancellationToken);
        if (exceeded || content.Length != command.SizeBytes || content.Length == 0)
        {
            var rejectedResult = await repository.PersistTechnicalOutcomeAsync(
                command, ETriagemInboundMediaOutcome.RejeitadaTamanho, sha256, cancellationToken);
            return new TriagemInboundMediaUseCaseResult(rejectedResult, IsIdempotencyConflict: false);
        }

        if (!TriagemInboundMediaTypeValidator.IsAccepted(command.MediaKind, command.ContentType, content))
        {
            var rejectedResult = await repository.PersistTechnicalOutcomeAsync(
                command, ETriagemInboundMediaOutcome.RejeitadaTipo, sha256, cancellationToken);
            return new TriagemInboundMediaUseCaseResult(rejectedResult, IsIdempotencyConflict: false);
        }

        var result = await repository.ProcessAcceptedMediaAsync(command, sha256, content, cancellationToken);

        if (result.Outcome == ETriagemInboundMediaOutcome.Ambigua)
        {
            logger.LogWarning(
                "[TRIAGEM-CHANNEL-MEDIA] Mídia recebida ambígua; aguardando resolução manual. " +
                "CorrelationId={CorrelationId} CandidateCount={CandidateCount}",
                command.CorrelationId,
                result.CandidateCount);
        }
        else
        {
            logger.LogInformation(
                "[TRIAGEM-CHANNEL-MEDIA] Mídia recebida processada. CorrelationId={CorrelationId} Outcome={Outcome}",
                command.CorrelationId,
                result.Outcome);
        }

        await ConductConversationAsync(command, result, cancellationToken);
        return new TriagemInboundMediaUseCaseResult(result, IsIdempotencyConflict: false);
    }

    private async Task ConductConversationAsync(TriagemInboundMediaCommand command,
        TriagemInboundMediaResult result, CancellationToken cancellationToken)
    {
        if ((result.Outcome is ETriagemInboundMediaOutcome.ColetaIniciada or ETriagemInboundMediaOutcome.Armazenada)
            && result.TriagemId is { } triagemId)
        {
            await llmConversationService.ConductAsync(
                command.TenantId, triagemId, result.MediaId, command.SenderPhone, cancellationToken);
        }
    }

    private static ETriagemInboundMediaOutcome? GetIgnoredOutcome(TriagemInboundMediaCommand command)
    {
        if (string.Equals(command.ConversationKind, "group", StringComparison.OrdinalIgnoreCase))
        {
            return ETriagemInboundMediaOutcome.IgnoradaGrupo;
        }

        return command.FromMe ? ETriagemInboundMediaOutcome.IgnoradaPropria : null;
    }

    /// <summary>
    /// Lê o stream do arquivo até <paramref name="maxBytes"/> + 1, computando o
    /// SHA-256 incrementalmente. Nunca materializa mais que isso em memória —
    /// um arquivo maior que o limite é interrompido cedo e marcado como
    /// excedido, sem completar o hash nem ser armazenado.
    /// </summary>
    private static async Task<(byte[] Content, string Sha256, bool Exceeded)> ReadCappedAsync(
        Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        long total = 0;
        var exceeded = false;
        int read;
        while ((read = await content.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
        {
            total += read;
            hasher.AppendData(chunk, 0, read);
            if (total > maxBytes)
            {
                exceeded = true;
                buffer.SetLength(0);
                continue;
            }

            if (!exceeded)
            {
                buffer.Write(chunk, 0, read);
            }
        }

        return (
            exceeded ? [] : buffer.ToArray(),
            Convert.ToHexStringLower(hasher.GetHashAndReset()),
            exceeded);
    }
}
