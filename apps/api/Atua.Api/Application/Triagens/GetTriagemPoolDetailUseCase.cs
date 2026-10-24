using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação de <see cref="IGetTriagemPoolDetailUseCase"/> (épico de pool
/// de triagem, "C2 — Detalhe").
/// </summary>
public sealed class GetTriagemPoolDetailUseCase(
    AtuaDbContext dbContext,
    ITriagemInboundMediaStorage mediaStorage,
    TimeProvider timeProvider) : IGetTriagemPoolDetailUseCase
{
    /// <summary>Validade do link pré-assinado de cada anexo (tempo suficiente para abrir o painel sem reemitir a cada render).</summary>
    public static readonly TimeSpan AttachmentUrlTtl = TimeSpan.FromMinutes(15);

    public async Task<TriagemPoolDetailResult> ExecuteAsync(Guid tenantId, Guid triagemId, Guid requestingUserId,
        CancellationToken cancellationToken)
    {
        var membership = await dbContext.TenantMemberships.AsNoTracking()
            .Where(item => item.UserId == requestingUserId && item.TenantId == tenantId)
            .Select(item => (ETenantMembershipRole?)item.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (membership is null) return TriagemPoolDetailResult.Failure(ETriagemPoolDetailOutcome.Forbidden);

        var isManager = membership is ETenantMembershipRole.Owner or ETenantMembershipRole.Admin;

        var triagem = await dbContext.Triagens.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == triagemId && item.TenantId == tenantId, cancellationToken);
        if (triagem is null) return TriagemPoolDetailResult.Failure(ETriagemPoolDetailOutcome.NotFound);

        // Office é gestão (somente leitura): além do pool e das assumidas,
        // expõe também as triagens ainda em coleta (pré-pool).
        var isVisible = Triagem.PoolStatuses.Contains(triagem.Status)
            || Triagem.InProgressStatuses.Contains(triagem.Status)
            || triagem.Status == ETriagemStatus.AssumidaPorOperador;
        if (!isVisible) return TriagemPoolDetailResult.Failure(ETriagemPoolDetailOutcome.NotFound);

        // Triagem assumida: só quem assumiu ou gestores (critério de aceite 8).
        if (triagem.Status == ETriagemStatus.AssumidaPorOperador
            && triagem.AssumidaPorUserId != requestingUserId
            && !isManager)
        {
            return TriagemPoolDetailResult.Failure(ETriagemPoolDetailOutcome.NotFound);
        }

        var workOrder = await dbContext.WorkOrders.AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == triagem.WorkOrderId, cancellationToken);

        var outboundCommands = await dbContext.TriagemChannelCommands.AsNoTracking()
            .Where(command => command.TriagemId == triagemId)
            .OrderBy(command => command.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var inboundMessages = await dbContext.TriagemInboundMessages.AsNoTracking()
            .Where(message => message.TriagemId == triagemId)
            .OrderBy(message => message.ReceivedAtUtc)
            .ToListAsync(cancellationToken);

        var inboundMedia = await dbContext.TriagemInboundMedia.AsNoTracking()
            .Where(media => media.TriagemId == triagemId)
            .OrderBy(media => media.ReceivedAtUtc)
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var attachments = new List<TriagemPoolAttachmentView>();
        foreach (var media in inboundMedia)
        {
            if (media.StorageKey is null) continue;

            var url = await mediaStorage.CreatePresignedReadUrlAsync(
                new TriagemInboundMediaStorageKey(tenantId, media.Id), AttachmentUrlTtl, cancellationToken);

            attachments.Add(new TriagemPoolAttachmentView(
                media.Id, media.MediaKind, media.ContentType, media.IsVoiceNote, media.DurationSeconds,
                media.Caption, media.ReceivedAtUtc, url, now.Add(AttachmentUrlTtl)));
        }

        var conversation = BuildConversation(outboundCommands, inboundMessages, inboundMedia);

        var detail = new TriagemPoolDetailView(
            triagem.Id,
            triagem.Status,
            BuildStateReason(triagem),
            triagem.UpdatedAtUtc,
            triagem.AssumidaPorUserId,
            triagem.AssumidaEmUtc,
            new TriagemPoolWorkOrderView(
                triagem.WorkOrderId,
                workOrder?.WorkOrderProviderNo,
                workOrder?.CustomerName,
                workOrder?.CustomerCpf,
                workOrder?.ContactPhone,
                workOrder?.ContactEmail,
                workOrder?.Address,
                workOrder?.ZipCode,
                workOrder?.CityName,
                workOrder?.StateName,
                workOrder?.ProductBrand,
                workOrder?.ProductModel,
                workOrder?.Symptom),
            BuildChecklist(triagem),
            attachments,
            conversation);

        return new TriagemPoolDetailResult(ETriagemPoolDetailOutcome.Success, detail);
    }

    private static IReadOnlyList<TriagemPoolChecklistItemView> BuildChecklist(Triagem triagem) =>
    [
        new TriagemPoolChecklistItemView(ETriagemChecklistItem.DescricaoDefeito, Required: true,
            !string.IsNullOrWhiteSpace(triagem.DescricaoDefeito), triagem.DescricaoDefeito),
        new TriagemPoolChecklistItemView(ETriagemChecklistItem.NotaFiscal, Required: true,
            triagem.NotaFiscalLegivel, triagem.NotaFiscalLegivel ? "Legível" : null),
        new TriagemPoolChecklistItemView(ETriagemChecklistItem.Etiqueta, Required: true,
            !string.IsNullOrWhiteSpace(triagem.EtiquetaModelo) && !string.IsNullOrWhiteSpace(triagem.EtiquetaSerie),
            !string.IsNullOrWhiteSpace(triagem.EtiquetaModelo) && !string.IsNullOrWhiteSpace(triagem.EtiquetaSerie)
                ? $"Modelo {triagem.EtiquetaModelo} / Série {triagem.EtiquetaSerie}"
                : null),
        new TriagemPoolChecklistItemView(ETriagemChecklistItem.MidiaDefeito, Required: false,
            triagem.MidiaDefeitoTipo is not null, triagem.MidiaDefeitoTipo?.ToString()),
        new TriagemPoolChecklistItemView(ETriagemChecklistItem.Endereco, Required: true,
            !string.IsNullOrWhiteSpace(triagem.EnderecoCompleto), triagem.EnderecoCompleto),
        new TriagemPoolChecklistItemView(ETriagemChecklistItem.PontoReferencia, Required: false,
            !string.IsNullOrWhiteSpace(triagem.PontoReferencia), triagem.PontoReferencia),
        new TriagemPoolChecklistItemView(ETriagemChecklistItem.Localizacao, Required: false,
            !string.IsNullOrWhiteSpace(triagem.Localizacao), triagem.Localizacao),
    ];

    private static string BuildStateReason(Triagem triagem) => triagem.Status switch
    {
        ETriagemStatus.Expirada =>
            "Prazo de 48h sem checklist mínimo completo — encaminhada ao pool para decisão humana.",
        ETriagemStatus.FalhaDeContato =>
            "Falha de contato: número inválido, sem WhatsApp, ou falha definitiva no envio do contato inicial.",
        ETriagemStatus.AguardandoIntervencao => BuildAguardandoIntervencaoReason(triagem),
        ETriagemStatus.LiberadaAoPool =>
            "Checklist mínimo completo — liberada para decisão de um operador do pool.",
        ETriagemStatus.AssumidaPorOperador => "Assumida por operador — atendimento exclusivo.",
        _ => triagem.Status.ToString(),
    };

    private static string BuildAguardandoIntervencaoReason(Triagem triagem)
    {
        if (triagem.DesviosConsecutivos >= Triagem.MaxDesviosConsecutivos)
        {
            return $"{Triagem.MaxDesviosConsecutivos} mensagens consecutivas fora de contexto — aguardando intervenção humana.";
        }

        var itemComMaisTentativas = triagem.TentativasInvalidasPorItem
            .Where(entry => entry.Value >= Triagem.MaxTentativasInvalidasPorItem)
            .Select(entry => entry.Key)
            .Cast<ETriagemChecklistItem?>()
            .FirstOrDefault();

        return itemComMaisTentativas is not null
            ? $"Mídia/resposta ilegível para '{itemComMaisTentativas}' após {Triagem.MaxTentativasInvalidasPorItem} tentativas — aguardando intervenção humana."
            : "Aguardando intervenção humana.";
    }

    private static IReadOnlyList<TriagemPoolConversationEntryView> BuildConversation(
        IReadOnlyList<TriagemChannelCommand> outboundCommands,
        IReadOnlyList<TriagemInboundMessage> inboundMessages,
        IReadOnlyList<TriagemInboundMedia> inboundMedia)
    {
        var entries = new List<TriagemPoolConversationEntryView>();

        entries.AddRange(outboundCommands.Select(command => new TriagemPoolConversationEntryView(
            command.CreatedAtUtc, "posto", "text", command.RenderedContent, null)));

        entries.AddRange(inboundMessages.Select(message => new TriagemPoolConversationEntryView(
            message.ReceivedAtUtc, "consumidor", message.Kind, message.Text, null)));

        entries.AddRange(inboundMedia.Select(media => new TriagemPoolConversationEntryView(
            media.ReceivedAtUtc, "consumidor", media.MediaKind.ToString(), media.Caption, media.Id)));

        return entries.OrderBy(entry => entry.AtUtc).ToArray();
    }
}
