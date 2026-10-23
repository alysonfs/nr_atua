using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Orquestra o loop conversacional da LLM dentro da Master API, sem worker
/// novo (plano M2, docs/implementation/PLANO-triagem-llm-jev.md §3/§5.3;
/// `triagem-llm-conducao.md`). Chamado de forma síncrona ao processamento
/// de cada mensagem inbound (<see cref="IngestTriagemChannelMessageUseCase"/>)
/// quando a triagem está em <see cref="ETriagemStatus.EmColeta"/>.
///
/// A LLM só propõe — toda transição de estado é decidida e aplicada pelo
/// domínio <see cref="Triagem"/> a partir da saída validada (nunca como
/// comando direto). A resposta a enviar reaproveita a mesma fila/outbox de
/// comandos de canal já usada para o template inicial (ADR-041/ADR-042),
/// marcada com <see cref="ETriagemChannelCommandKind.RespostaLlm"/> para que
/// o <c>complete</c> não reaplique a transição de aceite do template
/// inicial (ver <see cref="TriagemChannelCommandService"/>).
///
/// Pendência conhecida (reportada ao orchestrator/software-architect): o
/// contrato <see cref="TriagemLlmItemAtendido"/> (M1) carrega apenas um
/// texto de evidência — insuficiente para os dois campos estruturados que
/// a Etiqueta (modelo + série) e a MidiaDefeito (foto/vídeo) exigem. Por
/// isso, esta implementação aplica automaticamente via LLM apenas os cinco
/// demais itens do checklist (DescricaoDefeito, NotaFiscal, Endereco,
/// PontoReferencia, Localizacao); Etiqueta e MidiaDefeito continuam
/// exigindo evolução futura do contrato ou coleta assistida.
/// </summary>
public sealed class TriagemLlmConversationService(
    ITriagemRepository triagemRepository,
    ITriagemChannelCommandRepository commandRepository,
    ITriagemLlmConversationContextRepository contextRepository,
    ITriagemLlmClient llmClient,
    ITriagemLlmUsageRepository usageRepository,
    ITriagemLlmResponseVerifier responseVerifier,
    IOptions<TriagemLlmConversationOptions> options,
    TimeProvider timeProvider,
    ILogger<TriagemLlmConversationService> logger)
{
    /// <summary>
    /// Ordem fixa de prioridade dos itens obrigatórios para a pergunta de
    /// fallback/continuação — mesma ordem de `triagem-llm-conducao.md` §3.
    /// </summary>
    private static readonly ETriagemChecklistItem[] ChecklistOrder =
    [
        ETriagemChecklistItem.DescricaoDefeito,
        ETriagemChecklistItem.NotaFiscal,
        ETriagemChecklistItem.Etiqueta,
        ETriagemChecklistItem.MidiaDefeito,
        ETriagemChecklistItem.Endereco,
        ETriagemChecklistItem.PontoReferencia,
        ETriagemChecklistItem.Localizacao,
    ];

    public async Task ConductAsync(Guid tenantId, Guid triagemId, Guid inboundMessageId, string consumerPhone,
        CancellationToken cancellationToken)
    {
        if (!options.Value.IsEligible(tenantId, consumerPhone))
        {
            // Fora da allowlist ou flag desligada: comportamento atual
            // inalterado — nenhuma chamada à LLM, nenhum efeito.
            return;
        }

        var idempotencyKey = BuildIdempotencyKey(tenantId, inboundMessageId);
        if (await commandRepository.ExistsByIdempotencyKeyAsync(idempotencyKey, cancellationToken))
        {
            // Reentrega do mesmo evento inbound já processada — nunca
            // repete a chamada à LLM nem o efeito observável (plano M2,
            // "Idempotência, rate limit e expiração de 48h").
            return;
        }

        // FindByIdAsync (não FindByKeyAsync) devolve a entidade rastreada
        // pelo mesmo DbContext (Scoped) de commandRepository — mesmo padrão
        // de TriagemChannelCommandService.ApplyTriagemTransitionAsync: as
        // mutações feitas diretamente em `triagem` abaixo são persistidas
        // por commandRepository.SaveChangesAsync, sem precisar (nem poder,
        // ver FakeTriagemRepository) chamar SaveTransitionAsync aqui.
        var triagem = await triagemRepository.FindByIdAsync(triagemId, cancellationToken);
        if (triagem is null || triagem.Status != ETriagemStatus.EmColeta)
        {
            // Só conduz a conversa com a triagem em coleta (plano M2 §3,
            // item 3) — qualquer outro estado já passou para fora do loop
            // da LLM (`triagem-llm-conducao.md` §4: "a partir de
            // ASSUMIDA_POR_OPERADOR, EXPIRADA ou FALHA_DE_CONTATO, a API
            // não chama mais a LLM").
            return;
        }

        var now = timeProvider.GetUtcNow();

        var callCount = await usageRepository.CountCallsAsync(triagemId, cancellationToken);
        if (callCount >= options.Value.MaxLlmCallsPerTriagem)
        {
            logger.LogWarning(
                "[TRIAGEM-LLM] Orçamento de chamadas excedido. TriagemId={TriagemId} CallCount={CallCount} " +
                "Limite={Limite}", triagemId, callCount, options.Value.MaxLlmCallsPerTriagem);
            triagem.MarcarAguardandoIntervencao(now);
            await commandRepository.SaveChangesAsync(cancellationToken);
            return;
        }

        var context = await contextRepository.LoadAsync(tenantId, triagemId, triagem.WorkOrderId, cancellationToken);
        var request = BuildRequest(tenantId, triagemId, triagem, context);

        var result = await llmClient.ConduzirAsync(request, cancellationToken);

        await usageRepository.RecordAsync(BuildUsageRecord(tenantId, triagemId, result, now), cancellationToken);

        switch (result.Outcome)
        {
            case ETriagemLlmOutcome.ProviderUnavailable:
                // `triagem-llm-conducao.md` §4: falha persistente do
                // fornecedor → AguardandoIntervencao. Retentativa com
                // backoff é pendência explícita (não implementada nesta
                // fase — ver relato ao orchestrator): uma única tentativa
                // síncrona já é suficiente para não deixar o consumidor sem
                // resposta por tempo indefinido (o pool assume a partir daqui).
                logger.LogWarning(
                    "[TRIAGEM-LLM] Fornecedor indisponível. TriagemId={TriagemId} Motivo={Motivo}",
                    triagemId, result.ErrorReason);
                triagem.MarcarAguardandoIntervencao(now);
                await commandRepository.SaveChangesAsync(cancellationToken);
                return;

            case ETriagemLlmOutcome.InvalidOutput:
                // `triagem-llm-conducao.md` §4: saída fora do esquema
                // compartilha o mesmo limite/consequência de desvios
                // consecutivos (3) — não é tentativa inválida de um item
                // específico do checklist.
                logger.LogWarning(
                    "[TRIAGEM-LLM] Saída fora do esquema. TriagemId={TriagemId} Motivo={Motivo}",
                    triagemId, result.ErrorReason);
                triagem.RegistrarDesvio(now);
                await commandRepository.SaveChangesAsync(cancellationToken);
                return;

            case ETriagemLlmOutcome.Success:
                await HandleSuccessAsync(tenantId, triagemId, idempotencyKey, triagem, result.Payload!, now,
                    cancellationToken);
                return;

            default:
                throw new InvalidOperationException($"Outcome não tratado: {result.Outcome}.");
        }
    }

    private async Task HandleSuccessAsync(Guid tenantId, Guid triagemId, string idempotencyKey, Triagem triagem,
        TriagemLlmSuccessPayload payload, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ApplyItensAtendidos(triagem, payload.ItensAtendidos, now);

        if (payload.SinalDesvio.Detectado)
        {
            // Mesmo mecanismo já existente de desvio de contexto (contador
            // de 3 consecutivos) — a distinção fina entre "fora de escopo" e
            // "pedido de atendimento humano" fica para o motor JEV (plano
            // M3+); o contrato M1 não fornece informação estruturada
            // suficiente para diferenciar com segurança nesta fase.
            triagem.RegistrarDesvio(now);
        }

        if (triagem.Status == ETriagemStatus.EmColeta && triagem.ChecklistMinimoCompleto)
        {
            triagem.MarcarInformacoesSuficientes(now);
            triagem.LiberarAoPool(now);
        }

        if (triagem.Status == ETriagemStatus.AguardandoIntervencao)
        {
            // Atingiu o limite de tentativas inválidas por item ou de
            // desvios consecutivos neste turno — nenhuma mensagem
            // automática é enviada; o pool assume a partir daqui (mesmo
            // comportamento já existente para esses contadores).
            await commandRepository.SaveChangesAsync(cancellationToken);
            return;
        }

        var mensagem = ResolveOutboundMessage(triagem, payload.RespostaAoConsumidor);
        if (mensagem is null)
        {
            // Resposta bloqueada pelo verificador e sem pergunta de
            // fallback segura disponível (ex.: checklist já completo neste
            // turno) — nenhuma mensagem é enviada (plano M2 §3, item 5).
            await commandRepository.SaveChangesAsync(cancellationToken);
            return;
        }

        var command = new TriagemChannelCommand(
            Guid.CreateVersion7(),
            tenantId,
            triagem.IntegrationId,
            triagemId,
            triagem.WorkOrderId,
            templateId: "triagem-llm-response",
            templateVersion: 1,
            renderedContent: mensagem,
            idempotencyKey: idempotencyKey,
            correlationId: Guid.CreateVersion7(),
            now: now,
            recipientPhone: TriagemTestSafeguard.FixedRecipientPhone,
            kind: ETriagemChannelCommandKind.RespostaLlm);

        commandRepository.Add(command);

        // Mesmo DbContext compartilhado (Scoped) de triagemRepository —
        // persiste o comando e a transição da triagem na mesma unidade de
        // trabalho (mesmo princípio de
        // TriagemChannelCommandService.ApplyTriagemTransitionAsync).
        await commandRepository.SaveChangesAsync(cancellationToken);
    }

    private string? ResolveOutboundMessage(Triagem triagem, string respostaAoConsumidor)
    {
        var verification = responseVerifier.Verify(respostaAoConsumidor);
        if (verification.Permitido)
        {
            return respostaAoConsumidor;
        }

        logger.LogWarning(
            "[TRIAGEM-LLM] Resposta proposta bloqueada pelo verificador. TriagemId={TriagemId} Motivo={Motivo}",
            triagem.Id, verification.Motivo);

        if (triagem.Status != ETriagemStatus.EmColeta)
        {
            // Checklist já completo e liberado ao pool neste turno — não há
            // "próximo item pendente" para perguntar de fallback.
            return null;
        }

        var pendente = NextPendingItem(triagem);
        if (pendente is null)
        {
            return null;
        }

        var fallback = TriagemLlmFallbackQuestions.For(pendente.Value);
        var fallbackVerification = responseVerifier.Verify(fallback);
        return fallbackVerification.Permitido ? fallback : null;
    }

    private static void ApplyItensAtendidos(Triagem triagem, IReadOnlyList<TriagemLlmItemAtendido> itensAtendidos,
        DateTimeOffset now)
    {
        foreach (var item in itensAtendidos)
        {
            switch (item.Item)
            {
                case ETriagemChecklistItem.DescricaoDefeito:
                    triagem.RegistrarDescricaoDefeito(item.Evidencia, now);
                    break;
                case ETriagemChecklistItem.NotaFiscal:
                    triagem.RegistrarNotaFiscalLegivel(now);
                    break;
                case ETriagemChecklistItem.Endereco:
                    triagem.RegistrarEndereco(item.Evidencia, now);
                    break;
                case ETriagemChecklistItem.PontoReferencia:
                    triagem.RegistrarPontoReferencia(item.Evidencia, now);
                    break;
                case ETriagemChecklistItem.Localizacao:
                    triagem.RegistrarLocalizacao(item.Evidencia, now);
                    break;
                case ETriagemChecklistItem.Etiqueta:
                case ETriagemChecklistItem.MidiaDefeito:
                    // Pendência conhecida (ver documentação da classe):
                    // contrato M1 não fornece modelo/série nem tipo de
                    // mídia estruturados — não marcado automaticamente.
                    break;
            }
        }
    }

    private static ETriagemChecklistItem? NextPendingItem(Triagem triagem)
    {
        foreach (var item in ChecklistOrder)
        {
            if (!IsItemAtendido(triagem, item))
            {
                return item;
            }
        }

        return null;
    }

    private static bool IsItemAtendido(Triagem triagem, ETriagemChecklistItem item) => item switch
    {
        ETriagemChecklistItem.DescricaoDefeito => !string.IsNullOrWhiteSpace(triagem.DescricaoDefeito),
        ETriagemChecklistItem.NotaFiscal => triagem.NotaFiscalLegivel,
        ETriagemChecklistItem.Etiqueta =>
            !string.IsNullOrWhiteSpace(triagem.EtiquetaModelo) && !string.IsNullOrWhiteSpace(triagem.EtiquetaSerie),
        ETriagemChecklistItem.MidiaDefeito => triagem.MidiaDefeitoTipo is not null,
        ETriagemChecklistItem.Endereco => !string.IsNullOrWhiteSpace(triagem.EnderecoCompleto),
        ETriagemChecklistItem.PontoReferencia => !string.IsNullOrWhiteSpace(triagem.PontoReferencia),
        ETriagemChecklistItem.Localizacao => !string.IsNullOrWhiteSpace(triagem.Localizacao),
        _ => true,
    };

    private static TriagemLlmRequest BuildRequest(Guid tenantId, Guid triagemId, Triagem triagem,
        TriagemLlmConversationContext context)
    {
        var checklistState = ChecklistOrder
            .Select(item => new TriagemLlmChecklistItemState(item, IsItemAtendido(triagem, item)))
            .ToArray();

        return new TriagemLlmRequest(
            tenantId,
            triagemId,
            context.TenantNomeComercial,
            context.Produto,
            context.Marca,
            context.JaApresentouIdentidade,
            checklistState,
            context.HistoricoConversa,
            context.AnexosRecebidos);
    }

    private static TriagemLlmUsageRecord BuildUsageRecord(Guid tenantId, Guid triagemId, TriagemLlmResult result,
        DateTimeOffset now) =>
        new(
            tenantId,
            triagemId,
            result.Usage?.ModelId ?? "",
            result.Usage?.InputTokens ?? 0,
            result.Usage?.OutputTokens ?? 0,
            result.Usage is null ? 0 : (long)result.Usage.Latencia.TotalMilliseconds,
            result.Outcome,
            now);

    private static string BuildIdempotencyKey(Guid tenantId, Guid inboundMessageId) =>
        $"triagem-llm-response:{tenantId}:{inboundMessageId}";
}
