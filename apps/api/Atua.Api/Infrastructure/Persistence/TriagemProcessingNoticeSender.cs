using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Implementação de produção de <see cref="ITriagemProcessingNoticeSender"/>
/// (decisão do PO — `triagem-llm-conducao.md`). Cria um escopo de DI próprio
/// por chamada (<see cref="IServiceScopeFactory"/>, mesmo padrão de
/// <see cref="Jobs.TriagemOutboxDrainWorker"/>) para nunca compartilhar o
/// <c>AtuaDbContext</c> scoped do turno de <see cref="TriagemLlmConversationService"/>
/// ainda em execução concorrentemente.
/// </summary>
public sealed class TriagemProcessingNoticeSender(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<TriagemProcessingNoticeSender> logger) : ITriagemProcessingNoticeSender
{
    public const string TemplateId = "triagem-processing-notice";

    public async Task SendAsync(TriagemProcessingNoticeRequest request, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandRepository = scope.ServiceProvider.GetRequiredService<ITriagemChannelCommandRepository>();
        var responseVerifier = scope.ServiceProvider.GetRequiredService<ITriagemLlmResponseVerifier>();
        var messagePicker = scope.ServiceProvider.GetRequiredService<ITriagemProcessingNoticeMessagePicker>();

        var idempotencyKey = BuildIdempotencyKey(request.InboundMessageId);
        if (await commandRepository.ExistsByIdempotencyKeyAsync(idempotencyKey, cancellationToken))
        {
            // Reentrega do mesmo evento inbound — nunca duplica o aviso.
            return;
        }

        var mensagem = messagePicker.Pick();
        var verification = responseVerifier.Verify(mensagem);
        if (!verification.Permitido)
        {
            // Nunca deveria acontecer com as mensagens aprovadas, mas o
            // verificador continua sendo a última linha de defesa — mesmo
            // princípio de TriagemLlmConversationService.ResolveOutboundMessage.
            logger.LogWarning(
                "[TRIAGEM-PROCESSING-NOTICE] Mensagem de aviso bloqueada pelo verificador. TriagemId={TriagemId} " +
                "Motivo={Motivo}", request.TriagemId, verification.Motivo);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var command = new TriagemChannelCommand(
            Guid.CreateVersion7(),
            request.TenantId,
            request.IntegrationId,
            request.TriagemId,
            request.WorkOrderId,
            templateId: TemplateId,
            templateVersion: 1,
            renderedContent: mensagem,
            idempotencyKey: idempotencyKey,
            correlationId: Guid.CreateVersion7(),
            now: now,
            recipientPhone: TriagemTestSafeguard.ResolveRecipientPhone(request.ConsumerPhone),
            kind: ETriagemChannelCommandKind.AvisoProcessamento);

        commandRepository.Add(command);
        await commandRepository.SaveChangesAsync(cancellationToken);
    }

    internal static string BuildIdempotencyKey(Guid inboundMessageId) =>
        $"triagem-processing-notice:{inboundMessageId}";
}
