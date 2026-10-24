using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Caso de uso que classifica, via visão computacional no Bedrock, os
/// anexos de imagem ou PDF ainda não classificados de uma triagem (decisão do PO
/// de adiantar a conferência automática de nota fiscal e etiqueta via
/// WhatsApp), e aplica a regra determinística de checklist resultante.
/// Chamado por <see cref="TriagemLlmConversationService"/> a cada turno,
/// antes da chamada de conversa — nunca em paralelo a ela.
///
/// A LLM só é informada do fato (tipo detectado + legibilidade) — nunca da
/// imagem em si; quem decide marcar <see cref="ETriagemChecklistItem.NotaFiscal"/>
/// e <see cref="ETriagemChecklistItem.Etiqueta"/> como atendidos é esta
/// regra determinística, nunca a LLM conversacional.
/// </summary>
public interface IClassifyTriagemAttachmentsUseCase
{
    Task ClassifyPendingAsync(Guid tenantId, Triagem triagem, CancellationToken cancellationToken);

    /// <summary>
    /// Verifica se a triagem tem, neste turno, algum anexo de imagem ou PDF
    /// ainda não classificado (<see cref="TriagemInboundMedia.VisionClassifiedAtUtc"/>
    /// nulo) — usado por <see cref="TriagemLlmConversationService"/> para
    /// decidir se o turno está sujeito ao aviso de "imagem em análise"
    /// (decisão do PO). Deve ser chamado antes de <see cref="ClassifyPendingAsync"/>,
    /// que marca as mídias como classificadas.
    /// </summary>
    Task<bool> HasPendingAttachmentsAsync(Guid triagemId, CancellationToken cancellationToken);
}

public sealed class ClassifyTriagemAttachmentsUseCase(
    ITriagemAttachmentVisionRepository mediaRepository,
    ITriagemInboundMediaStorage mediaStorage,
    ITriagemAttachmentVisionClient visionClient,
    ITriagemLlmUsageRepository usageRepository,
    IOptions<TriagemLlmConversationOptions> options,
    TimeProvider timeProvider,
    ILogger<ClassifyTriagemAttachmentsUseCase> logger) : IClassifyTriagemAttachmentsUseCase
{
    /// <summary>
    /// Limite de tamanho de imagem aceito pelo bloco de imagem da Converse
    /// API do Bedrock (~3.75 MiB). Acima disso, a imagem nunca é enviada ao
    /// modelo — o item correspondente do checklist fica pendente.
    /// </summary>
    internal const long MaxImageBytes = 3_932_160;
    internal const long MaxPdfBytes = 4_500_000;

    /// <summary>Content types de imagem aceitos pela Converse API — os únicos efetivamente enviados ao classificador.</summary>
    private static readonly HashSet<string> SupportedImageContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public async Task<bool> HasPendingAttachmentsAsync(Guid triagemId, CancellationToken cancellationToken)
    {
        var imagens = await mediaRepository.FindClassifiableMediaAsync(triagemId, cancellationToken);
        return imagens.Any(media => media.VisionClassifiedAtUtc is null);
    }

    public async Task ClassifyPendingAsync(Guid tenantId, Triagem triagem, CancellationToken cancellationToken)
    {
        var imagens = await mediaRepository.FindClassifiableMediaAsync(triagem.Id, cancellationToken);
        if (imagens.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var houveClassificacaoNova = false;

        foreach (var media in imagens.Where(media => media.VisionClassifiedAtUtc is null))
        {
            // Reserva uma chamada para responder ao consumidor neste turno.
            if (await usageRepository.CountCallsAsync(triagem.Id, cancellationToken)
                >= options.Value.MaxLlmCallsPerTriagem - 1)
            {
                logger.LogWarning(
                    "[TRIAGEM-VISION] Limite de chamadas de visão atingido; reservando resposta. TriagemId={TriagemId}",
                    triagem.Id);
                break;
            }

            if (!(await TryClassifyOneAsync(tenantId, triagem.Id, media, now, cancellationToken)))
            {
                continue;
            }

            houveClassificacaoNova = true;
        }

        if (houveClassificacaoNova)
        {
            await mediaRepository.SaveChangesAsync(cancellationToken);
        }

        ApplyDeterministicChecklistRules(triagem, imagens, now);
    }

    /// <summary>Classifica uma única mídia. Retorna <c>false</c> quando o item fica pendente (fora de escopo, tamanho ou falha do provedor).</summary>
    private async Task<bool> TryClassifyOneAsync(Guid tenantId, Guid triagemId, TriagemInboundMedia media,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var isPdf = media.MediaKind == ETriagemInboundMediaKind.Document
            && string.Equals(media.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        if (media.StorageKey is null || (!isPdf && !SupportedImageContentTypes.Contains(media.ContentType)))
        {
            return false;
        }

        if (media.SizeBytes > (isPdf ? MaxPdfBytes : MaxImageBytes))
        {
            logger.LogInformation(
                "[TRIAGEM-VISION] Anexo {MediaId} ({SizeBytes} bytes) excede o limite do Bedrock — não classificado, item fica pendente.",
                media.Id, media.SizeBytes);
            return false;
        }

        TriagemInboundMediaContent? content;
        try
        {
            content = await mediaStorage.GetAsync(
                new TriagemInboundMediaStorageKey(tenantId, media.Id), cancellationToken);
        }
        catch (TriagemInboundMediaStorageException exception)
        {
            logger.LogWarning(exception,
                "[TRIAGEM-VISION] Falha ao ler a mídia {MediaId} do armazenamento — item fica pendente.", media.Id);
            return false;
        }

        if (content is null)
        {
            logger.LogWarning(
                "[TRIAGEM-VISION] Objeto da mídia {MediaId} ausente no armazenamento — item fica pendente.",
                media.Id);
            return false;
        }

        var request = new TriagemAttachmentVisionRequest(tenantId, triagemId, media.Id, content.Content, content.ContentType);
        var result = await visionClient.ClassificarAsync(request, cancellationToken);

        await usageRepository.RecordAsync(
            new TriagemLlmUsageRecord(
                tenantId,
                triagemId,
                result.Usage?.ModelId ?? "",
                result.Usage?.InputTokens ?? 0,
                result.Usage?.OutputTokens ?? 0,
                result.Usage is null ? 0 : (long)result.Usage.Latencia.TotalMilliseconds,
                result.Outcome,
                now),
            cancellationToken);

        if (result.Outcome != ETriagemLlmOutcome.Success || result.Payload is null)
        {
            // Falha do provedor ou saída fora do esquema: nunca quebra o
            // fluxo da triagem — o item simplesmente fica pendente para
            // nova tentativa em um turno futuro (nenhum marcador de
            // idempotência é gravado).
            logger.LogWarning(
                "[TRIAGEM-VISION] Falha ao classificar a mídia {MediaId}: {Motivo} — item fica pendente.",
                media.Id, result.ErrorReason);
            return false;
        }

        media.RegistrarClassificacaoVisao(
            result.Payload.Tipo, result.Payload.Legivel, result.Payload.Modelo, result.Payload.NumeroSerie,
            result.Payload.Confianca, now);
        return true;
    }

    /// <summary>
    /// Regra determinística (nunca decidida pela LLM): nota fiscal atendida
    /// quando o tipo detectado é <see cref="ETriagemAttachmentVisionTipo.NotaFiscal"/>
    /// e legível; etiqueta atendida quando o tipo é
    /// <see cref="ETriagemAttachmentVisionTipo.Etiqueta"/>, legível e com
    /// modelo e número de série não vazios. Reaplicada a cada turno sobre
    /// todas as imagens já classificadas desta triagem (idempotente — só
    /// muta o domínio quando o item ainda não estava atendido).
    /// </summary>
    private static void ApplyDeterministicChecklistRules(Triagem triagem, IReadOnlyList<TriagemInboundMedia> imagens,
        DateTimeOffset now)
    {
        foreach (var media in imagens)
        {
            if (media.VisionTipoDetectado is null || media.VisionLegivel is not true)
            {
                continue;
            }

            switch (media.VisionTipoDetectado)
            {
                case ETriagemAttachmentVisionTipo.NotaFiscal when !triagem.NotaFiscalLegivel:
                    triagem.RegistrarNotaFiscalLegivel(now);
                    break;

                case ETriagemAttachmentVisionTipo.Etiqueta
                    when !string.IsNullOrWhiteSpace(media.VisionModelo)
                        && !string.IsNullOrWhiteSpace(media.VisionNumeroSerie)
                        && (string.IsNullOrWhiteSpace(triagem.EtiquetaModelo) || string.IsNullOrWhiteSpace(triagem.EtiquetaSerie)):
                    triagem.RegistrarEtiquetaLegivel(media.VisionModelo!, media.VisionNumeroSerie!, now);
                    break;
            }
        }
    }
}
