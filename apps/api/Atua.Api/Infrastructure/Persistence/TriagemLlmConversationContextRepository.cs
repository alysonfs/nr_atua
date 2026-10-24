using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Implementação EF Core de <see cref="ITriagemLlmConversationContextRepository"/>
/// (plano M2). Mescla o histórico de mensagens inbound (turnos do
/// consumidor) com o conteúdo já renderizado dos comandos de canal (turnos
/// da assistente, incluindo o template inicial) em ordem cronológica,
/// restrito a uma janela recente — nunca envia a conversa inteira à LLM
/// (minimização, `triagem-llm-conducao.md` §2).
/// </summary>
public sealed class TriagemLlmConversationContextRepository(AtuaDbContext dbContext)
    : ITriagemLlmConversationContextRepository
{
    /// <summary>Janela de histórico enviada à LLM a cada chamada — texto apenas, sem mídia binária.</summary>
    private const int HistoryWindowSize = 20;

    /// <summary>Janela de anexos recentes informados como metadados à LLM.</summary>
    private const int AttachmentWindowSize = 10;

    public async Task<TriagemLlmConversationContext> LoadAsync(Guid tenantId, Guid triagemId, Guid workOrderId,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var workOrder = await dbContext.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);

        var jaApresentouIdentidade = await dbContext.TriagemChannelCommands.AsNoTracking()
            .AnyAsync(command => command.TriagemId == triagemId
                && command.Kind == ETriagemChannelCommandKind.RespostaLlm, cancellationToken);

        var mensagens = await dbContext.TriagemInboundMessages.AsNoTracking()
            .Where(message => message.TriagemId == triagemId && message.Text != null)
            .OrderByDescending(message => message.ReceivedAtUtc)
            .Take(HistoryWindowSize)
            .Select(message => new { message.Text, message.ReceivedAtUtc })
            .ToListAsync(cancellationToken);

        var comandos = await dbContext.TriagemChannelCommands.AsNoTracking()
            .Where(command => command.TriagemId == triagemId)
            .OrderByDescending(command => command.CreatedAtUtc)
            .Take(HistoryWindowSize)
            .Select(command => new { command.RenderedContent, command.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var anexos = await dbContext.TriagemInboundMedia.AsNoTracking()
            .Where(media => media.TriagemId == triagemId)
            .OrderByDescending(media => media.ReceivedAtUtc)
            .Take(AttachmentWindowSize)
            .Select(media => new
            {
                media.Id,
                media.MediaKind,
                media.VisionTipoDetectado,
                media.VisionLegivel,
                media.Caption,
                media.ReceivedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var historico = mensagens
            .Select(message => (Turno: new TriagemLlmConversationTurn(
                ETriagemLlmConversationRole.Consumidor, message.Text!), message.ReceivedAtUtc))
            .Concat(comandos.Select(command => (Turno: new TriagemLlmConversationTurn(
                ETriagemLlmConversationRole.Assistente, command.RenderedContent), ReceivedAtUtc: command.CreatedAtUtc)))
            .Concat(anexos.Select(media => (Turno: new TriagemLlmConversationTurn(
                ETriagemLlmConversationRole.Consumidor,
                $"[Anexo recebido: {media.MediaKind}]"
                    + (string.IsNullOrWhiteSpace(media.Caption) ? "" : $" Legenda: {media.Caption}")),
                media.ReceivedAtUtc)))
            .OrderBy(entry => entry.ReceivedAtUtc)
            .TakeLast(HistoryWindowSize)
            .Select(entry => entry.Turno)
            .ToList();

        var anexosMetadata = anexos
            .Select(media => new TriagemLlmAttachmentMetadata(
                media.Id,
                media.MediaKind,
                ChecklistItemCandidato: null,
                VisionFato: media.VisionTipoDetectado is { } tipo && media.VisionLegivel is { } legivel
                    ? new TriagemLlmAttachmentVisionFato(tipo, legivel)
                    : null))
            .ToList();

        return new TriagemLlmConversationContext(
            tenant?.Name ?? "",
            workOrder?.ProductModel ?? "produto não informado",
            workOrder?.ProductBrand,
            jaApresentouIdentidade,
            historico,
            anexosMetadata);
    }
}
