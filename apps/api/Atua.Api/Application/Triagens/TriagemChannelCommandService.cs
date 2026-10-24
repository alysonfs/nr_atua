using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Orquestra os endpoints internos do processo do canal: <c>claim</c> e
/// <c>complete</c> (ADR-042). Tenant e integração sempre vêm exclusivamente
/// dos claims da credencial de serviço autenticada — nunca do corpo da
/// requisição (mesmo princípio de <c>ImmediateCollectionCommandService</c>).
/// </summary>
public sealed class TriagemChannelCommandService(
    ITriagemChannelCommandRepository commandRepository,
    ITriagemRepository triagemRepository,
    TimeProvider timeProvider,
    IOptions<TriagemChannelOptions> options,
    ILogger<TriagemChannelCommandService> logger)
{
    // -----------------------------------------------------------------------
    // Claim
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reivindica atomicamente o próximo comando elegível da
    /// tenant/integração informada. Retorna <c>null</c> quando não há
    /// comando elegível (→ 204 No Content).
    /// </summary>
    public async Task<TriagemChannelCommand?> ClaimAsync(Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseDuration = TimeSpan.FromSeconds(options.Value.LeaseSeconds);

        var command = await commandRepository.ClaimNextAsync(tenantId, integrationId, leaseDuration, now,
            cancellationToken);

        if (command is not null)
        {
            logger.LogInformation(
                "[TRIAGEM-CHANNEL] Comando reivindicado. CommandId={CommandId} TriagemId={TriagemId} " +
                "AttemptCount={AttemptCount}", command.Id, command.TriagemId, command.AttemptCount);
        }

        return command;
    }

    // -----------------------------------------------------------------------
    // Retransmissão
    // -----------------------------------------------------------------------

    public static readonly TimeSpan RetransmissionWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Recupera o texto de um comando concluído com sucesso na última hora
    /// cujo id de mensagem WhatsApp (derivado da chave de idempotência, como
    /// no canal) corresponde a <paramref name="messageId"/>. Não cria nem
    /// reabre comandos; serve apenas ao pedido de retransmissão do aparelho.
    /// </summary>
    public async Task<TriagemChannelCommand?> FindForRetransmissionAsync(Guid tenantId, Guid integrationId,
        string messageId, CancellationToken cancellationToken)
    {
        var since = timeProvider.GetUtcNow() - RetransmissionWindow;
        var commands = await commandRepository.ListCompletedSinceAsync(tenantId, integrationId, since,
            cancellationToken);
        var match = commands.FirstOrDefault(command =>
            string.Equals(MessageIdFor(command.IdempotencyKey), messageId, StringComparison.Ordinal));

        // Nunca loga messageId, IdempotencyKey, telefone ou conteúdo — apenas o
        // identificador interno (seguro) do comando, quando houver correspondência.
        if (match is not null)
        {
            logger.LogInformation(
                "[TRIAGEM-CHANNEL] Retransmissão reidratada via API. CommandId={CommandId}", match.Id);
        }
        else
        {
            logger.LogInformation(
                "[TRIAGEM-CHANNEL] Retransmissão sem correspondência elegível na janela de {WindowMinutes} min " +
                "(candidatos avaliados: {CandidateCount}).", RetransmissionWindow.TotalMinutes, commands.Count);
        }

        return match;
    }

    /// <summary>Mesma derivação de <c>messageIdForIdempotencyKey</c> do canal.</summary>
    public static string MessageIdFor(string idempotencyKey)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(idempotencyKey));
        return Convert.ToHexString(hash)[..32];
    }

    // -----------------------------------------------------------------------
    // Complete
    // -----------------------------------------------------------------------

    /// <summary>
    /// Conclui o comando com o resultado informado pelo canal (ADR-042).
    /// Idempotente: um comando já em estado terminal (<c>Completed</c>/<c>Failed</c>)
    /// retorna o mesmo resultado sem reaplicar nenhum efeito. Retorna
    /// <c>null</c> quando <paramref name="commandId"/> não pertence à
    /// tenant/integração informada.
    ///
    /// <paramref name="claimedAtUtc"/> é o token de lease devolvido pelo
    /// <c>claim</c> (<see cref="TriagemChannelCommand.ClaimedAtUtc"/>) — o
    /// mesmo papel de concorrência otimista de <c>TriagemTriggerOutbox.ClaimedAtUtc</c>.
    /// O efeito só é aplicado quando o comando ainda está <c>Claimed</c> e o
    /// token apresentado é exatamente o vigente; caso contrário, o chamador
    /// já perdeu a posse (lease expirada e, possivelmente, substituída por
    /// outro claim) e o resultado é <see cref="ETriagemChannelCompleteOutcome.LeaseLost"/>
    /// — nenhum efeito é aplicado ao comando ou à triagem.
    /// </summary>
    public async Task<TriagemChannelCompleteResult?> CompleteAsync(
        Guid commandId, Guid tenantId, Guid integrationId, DateTimeOffset claimedAtUtc,
        ETriagemChannelSendOutcome outcome, string? errorCode, string? errorDescription,
        CancellationToken cancellationToken)
    {
        var command = await commandRepository.FindByIdAsync(commandId, tenantId, integrationId, cancellationToken);
        if (command is null) return null;

        var now = timeProvider.GetUtcNow();

        if (command.Status is ETriagemChannelCommandStatus.Completed or ETriagemChannelCommandStatus.Failed)
        {
            // Idempotência (ADR-039/ADR-042): reentrega de um comando já
            // concluído retorna o resultado conhecido, sem repetir o efeito —
            // mesmo quando o token de lease apresentado não é mais o
            // vigente (o comando já não está sob lease nenhuma).
            return new TriagemChannelCompleteResult(ETriagemChannelCompleteOutcome.Applied, command.Id,
                command.Status, command.CompletedAtUtc ?? now);
        }

        if (command.Status != ETriagemChannelCommandStatus.Claimed || command.ClaimedAtUtc != claimedAtUtc)
        {
            // Pending: a lease expirou e o comando voltou a ser elegível; ou
            // Claimed com outro token: a lease expirou e foi substituída por
            // um novo claim. Em ambos os casos este chamador perdeu a posse
            // — nunca força a transição sobre um comando que não está mais
            // sob a lease apresentada.
            logger.LogWarning(
                "[TRIAGEM-CHANNEL] Complete recebido com lease perdida. CommandId={CommandId} Status={Status} " +
                "TokenApresentado={ClaimedAtUtc}", command.Id, command.Status, claimedAtUtc);
            return new TriagemChannelCompleteResult(ETriagemChannelCompleteOutcome.LeaseLost, command.Id,
                command.Status, command.CompletedAtUtc ?? now);
        }

        switch (outcome)
        {
            case ETriagemChannelSendOutcome.Success:
                command.MarkCompletedSuccess(now);
                if (command.Kind == ETriagemChannelCommandKind.TemplateInicial)
                {
                    await ApplyTriagemTransitionAsync(command.TriagemId,
                        triagem => triagem.MarcarContatoIniciado(now), cancellationToken);
                }

                // RespostaLlm (plano M2): a transição da triagem já foi
                // decidida e aplicada pelo domínio Triagem no momento em que
                // a resposta foi montada (TriagemLlmConversationService) —
                // o complete deste comando só confirma o envio do texto,
                // nunca reaplica nem reinterpreta uma transição de estado.
                break;

            case ETriagemChannelSendOutcome.PermanentFailure:
                command.MarkCompletedPermanentFailure(errorCode, now);
                if (command.Kind == ETriagemChannelCommandKind.TemplateInicial)
                {
                    await ApplyTriagemTransitionAsync(command.TriagemId,
                        triagem => triagem.MarcarFalhaDeContato(now), cancellationToken);
                }

                break;

            case ETriagemChannelSendOutcome.TransientFailure:
                command.RecordTransientFailure(errorCode, now);
                if (command.Status == ETriagemChannelCommandStatus.Failed
                    && command.Kind == ETriagemChannelCommandKind.TemplateInicial)
                {
                    // Quinta falha transitória consecutiva (ADR-042).
                    await ApplyTriagemTransitionAsync(command.TriagemId,
                        triagem => triagem.MarcarFalhaDeContato(now), cancellationToken);
                }

                break;

            default:
                throw new InvalidOperationException($"Outcome não tratado: {outcome}.");
        }

        await commandRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "[TRIAGEM-CHANNEL] Comando concluído. CommandId={CommandId} Outcome={Outcome} Status={Status} " +
            "AttemptCount={AttemptCount}", command.Id, outcome, command.Status, command.AttemptCount);

        return new TriagemChannelCompleteResult(ETriagemChannelCompleteOutcome.Applied, command.Id, command.Status,
            command.CompletedAtUtc ?? now);
    }

    /// <summary>
    /// Aplica a transição à triagem referenciada pelo comando, na mesma
    /// unidade de trabalho (persistida junto com o comando por
    /// <see cref="ITriagemChannelCommandRepository.SaveChangesAsync"/> —
    /// mesmo <c>DbContext</c> compartilhado, escopo por requisição). Nunca
    /// reaplica a transição quando a triagem já avançou além de
    /// <see cref="ETriagemStatus.AguardandoEnvio"/> por outro caminho.
    /// </summary>
    private async Task ApplyTriagemTransitionAsync(Guid triagemId, Action<Triagem> transition,
        CancellationToken cancellationToken)
    {
        var triagem = await triagemRepository.FindByIdAsync(triagemId, cancellationToken);
        if (triagem is null)
        {
            throw new InvalidOperationException(
                $"Triagem '{triagemId}' referenciada pelo comando de canal não foi encontrada.");
        }

        if (triagem.Status != ETriagemStatus.AguardandoEnvio)
        {
            return;
        }

        transition(triagem);
    }
}

/// <summary>
/// Resultado do <c>complete</c> do comando de canal — nunca expõe a entidade
/// de domínio/EF. <see cref="Outcome"/> distingue efeito aplicado (inclusive
/// replay idempotente terminal) de lease perdida (ADR-042, correção de
/// concorrência do token de lease) — o endpoint mapeia
/// <see cref="ETriagemChannelCompleteOutcome.LeaseLost"/> para HTTP 409.
/// </summary>
public sealed record TriagemChannelCompleteResult(ETriagemChannelCompleteOutcome Outcome, Guid CommandId,
    ETriagemChannelCommandStatus Status, DateTimeOffset CompletedAtUtc);
