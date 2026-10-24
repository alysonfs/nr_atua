using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de <see cref="TriagemLlmConversationService"/> (plano M2,
/// docs/implementation/PLANO-triagem-llm-jev.md §3/§5.3): fluxo feliz até
/// <see cref="ETriagemStatus.LiberadaAoPool"/>, fallback por verificador,
/// provedor indisponível, flag desligada, fora da allowlist, idempotência
/// por mensagem inbound. O "complete de resposta LLM não altera estado" é
/// coberto por <see cref="TriagemChannelCommandServiceTests"/> (mesma
/// classe que decide o branch por <see cref="ETriagemChannelCommandKind"/>).
/// </summary>
public class TriagemLlmConversationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        TriagemLlmConversationService Service,
        FakeTriagemRepository TriagemRepository,
        FakeTriagemChannelCommandRepository CommandRepository,
        FakeTriagemLlmUsageRepository UsageRepository,
        FakeTriagemLlmClient LlmClient,
        FakeTriagemLlmRetryDelay RetryDelay,
        FakeClassifyTriagemAttachmentsUseCase AttachmentVisionUseCase,
        Guid TenantId,
        Guid IntegrationId,
        Guid TriagemId,
        Guid WorkOrderId);

    private static Fixture CreateFixture(Func<Guid, TriagemLlmConversationOptions>? optionsFactory = null,
        Action<Triagem>? seedChecklist = null)
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();

        var triagemRepository = new FakeTriagemRepository();
        var key = new TriagemIdempotencyKey(tenantId, integrationId, $"OS-{Guid.NewGuid()}");
        var candidate = new Triagem(triagemId, tenantId, integrationId, workOrderId, Now);
        candidate.MarcarContatoIniciado(Now);
        candidate.IniciarColeta(Now);
        seedChecklist?.Invoke(candidate);

        // GetOrAddAsync armazena a própria instância `candidate` no fake —
        // mutações subsequentes feitas diretamente em `candidate` (via
        // FindByIdAsync, que devolve a mesma referência) são observáveis
        // pelo serviço, mesmo DbContext compartilhado simulado.
        triagemRepository.GetOrAddAsync(key, candidate, CancellationToken.None).GetAwaiter().GetResult();

        var commandRepository = new FakeTriagemChannelCommandRepository();
        var usageRepository = new FakeTriagemLlmUsageRepository();
        var llmClient = new FakeTriagemLlmClient();
        var contextRepository = new FakeTriagemLlmConversationContextRepository();
        var retryDelay = new FakeTriagemLlmRetryDelay();
        var attachmentVisionUseCase = new FakeClassifyTriagemAttachmentsUseCase();
        var effectiveOptions = optionsFactory?.Invoke(tenantId) ?? new TriagemLlmConversationOptions
        {
            ConversationEnabled = true,
            AllowedTenantIds = [tenantId],
            AllowedPhones = ["+5584994241248"],
        };

        var service = new TriagemLlmConversationService(
            triagemRepository,
            commandRepository,
            contextRepository,
            llmClient,
            usageRepository,
            new TriagemLlmResponseVerifier(),
            retryDelay,
            attachmentVisionUseCase,
            Options.Create(effectiveOptions),
            new FakeTimeProvider(Now),
            NullLogger<TriagemLlmConversationService>.Instance);

        return new Fixture(service, triagemRepository, commandRepository, usageRepository, llmClient, retryDelay,
            attachmentVisionUseCase, tenantId, integrationId, triagemId, workOrderId);
    }

    /// <summary>Marca os dois itens que o contrato M1 não permite marcar automaticamente via LLM (pendência documentada).</summary>
    private static void SeedEtiquetaEMidia(Triagem triagem)
    {
        triagem.RegistrarEtiquetaLegivel("Modelo-X", "Serie-123", Now);
        triagem.RegistrarMidiaDefeito(ETriagemMidiaDefeitoTipo.Foto, Now);
    }

    [Fact]
    public async Task ConductAsync_FluxoFeliz_AplicaItensAtendidosEALiberaAoPoolQuandoChecklistCompleto()
    {
        var fixture = CreateFixture(seedChecklist: triagem =>
        {
            SeedEtiquetaEMidia(triagem);
            triagem.RegistrarNotaFiscalLegivel(Now);
        });
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Success(
            new TriagemLlmSuccessPayload(
                "Obrigado! Recebemos todas as informações necessárias para seguir com a sua triagem.",
                new TriagemLlmCamposEstruturados(null, null, null, null),
                [
                    new TriagemLlmItemAtendido(ETriagemChecklistItem.DescricaoDefeito, "Não gela mais"),
                    new TriagemLlmItemAtendido(ETriagemChecklistItem.NotaFiscal, "Nota fiscal legível recebida"),
                    new TriagemLlmItemAtendido(ETriagemChecklistItem.Endereco, "Rua Teste, 123, Bairro, Cidade"),
                ],
                new TriagemLlmSinalDesvio(Detectado: false, Motivo: null)),
            new TriagemLlmUsageInfo("modelo-teste", 100, 40, TimeSpan.FromMilliseconds(500))));

        var messageId = Guid.NewGuid();
        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, messageId,
            "+5584994241248", CancellationToken.None);

        var triagem = await fixture.TriagemRepository.FindByIdAsync(fixture.TriagemId, CancellationToken.None);
        Assert.NotNull(triagem);
        Assert.Equal(ETriagemStatus.LiberadaAoPool, triagem!.Status);
        Assert.True(triagem.ChecklistMinimoCompleto);

        var command = Assert.Single(fixture.CommandRepository.Commands);
        Assert.Equal(ETriagemChannelCommandKind.RespostaLlm, command.Kind);
        Assert.Equal("Obrigado! Recebemos todas as informações necessárias para seguir com a sua triagem.",
            command.RenderedContent);
        Assert.Equal(TriagemTestSafeguard.FixedRecipientPhone, command.RecipientPhone);

        Assert.Single(fixture.UsageRepository.Records);
        Assert.Equal(ETriagemLlmOutcome.Success, fixture.UsageRepository.Records[0].Outcome);
    }

    [Fact]
    public async Task ConductAsync_NotaFiscalPropostaSomentePorTexto_NaoValidaDocumento()
    {
        var fixture = CreateFixture();
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Success(
            new TriagemLlmSuccessPayload(
                "Pode enviar a nota fiscal?",
                new TriagemLlmCamposEstruturados(null, null, null, null),
                [new TriagemLlmItemAtendido(ETriagemChecklistItem.NotaFiscal, "Já enviei a nota fiscal")],
                new TriagemLlmSinalDesvio(false, null)),
            new TriagemLlmUsageInfo("teste", 10, 5, TimeSpan.Zero)));

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, Guid.NewGuid(),
            "+5584994241248", CancellationToken.None);

        var triagem = await fixture.TriagemRepository.FindByIdAsync(fixture.TriagemId, CancellationToken.None);
        Assert.False(triagem!.NotaFiscalLegivel);
        Assert.Equal(ETriagemStatus.EmColeta, triagem.Status);
    }

    [Fact]
    public async Task ConductAsync_ChamaClassificacaoDeAnexosDeImagemAntesDaConversa()
    {
        // Decisão do PO (adiantar a visão computacional no Bedrock): a cada
        // turno elegível, ClassifyTriagemAttachmentsUseCase é chamado antes
        // da chamada de conversa — nunca pulado, independente do resultado.
        var fixture = CreateFixture();
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Success(
            new TriagemLlmSuccessPayload(
                "Certo, vamos continuar.",
                new TriagemLlmCamposEstruturados(null, null, null, null),
                [],
                new TriagemLlmSinalDesvio(Detectado: false, Motivo: null)),
            new TriagemLlmUsageInfo("modelo-teste", 10, 10, TimeSpan.FromMilliseconds(100))));

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, Guid.NewGuid(),
            "+5584994241248", CancellationToken.None);

        var call = Assert.Single(fixture.AttachmentVisionUseCase.Calls);
        Assert.Equal(fixture.TenantId, call.TenantId);
        Assert.Equal(fixture.TriagemId, call.TriagemId);
    }

    [Fact]
    public async Task ConductAsync_RespostaBloqueadaPeloVerificador_EnviaPerguntaDeFallbackDoProximoItemPendente()
    {
        var fixture = CreateFixture();
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Success(
            new TriagemLlmSuccessPayload(
                "Vamos agendar sua visita técnica para amanhã.",
                new TriagemLlmCamposEstruturados(null, null, null, null),
                ItensAtendidos: [],
                SinalDesvio: new TriagemLlmSinalDesvio(Detectado: false, Motivo: null)),
            new TriagemLlmUsageInfo("modelo-teste", 50, 20, TimeSpan.FromMilliseconds(300))));

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, Guid.NewGuid(),
            "+5584994241248", CancellationToken.None);

        var triagem = await fixture.TriagemRepository.FindByIdAsync(fixture.TriagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.EmColeta, triagem!.Status);

        var command = Assert.Single(fixture.CommandRepository.Commands);
        Assert.Equal(TriagemLlmFallbackQuestions.For(ETriagemChecklistItem.DescricaoDefeito), command.RenderedContent);
    }

    [Fact]
    public async Task ConductAsync_ProvedorIndisponivelEmTodasAsTentativas_MoveParaAguardandoIntervencaoAposBackoff()
    {
        // Decisão do PO (2026-10-23): até 2 novas tentativas (3 chamadas no
        // total) com backoff exponencial (1s, 2s) antes de desistir.
        var fixture = CreateFixture();
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Unavailable("timeout"));
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Unavailable("timeout"));
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Unavailable("timeout"));

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, Guid.NewGuid(),
            "+5584994241248", CancellationToken.None);

        var triagem = await fixture.TriagemRepository.FindByIdAsync(fixture.TriagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.AguardandoIntervencao, triagem!.Status);
        Assert.Empty(fixture.CommandRepository.Commands);

        // 3 tentativas, cada uma registrada em uso (inclusive as que falham).
        Assert.Equal(3, fixture.UsageRepository.Records.Count);
        Assert.All(fixture.UsageRepository.Records,
            record => Assert.Equal(ETriagemLlmOutcome.ProviderUnavailable, record.Outcome));

        // Backoff exponencial entre as 3 tentativas: 1s antes da 2ª, 2s antes da 3ª.
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], fixture.RetryDelay.RequestedDelays);
    }

    [Fact]
    public async Task ConductAsync_ProvedorIndisponivelNaPrimeiraTentativa_SucessoNaSegundaTentativaContinuaFluxo()
    {
        // Decisão do PO (2026-10-23): sucesso em qualquer tentativa dentro
        // do limite interrompe o retry e segue o fluxo normal.
        var fixture = CreateFixture();
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Unavailable("timeout"));
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Success(
            new TriagemLlmSuccessPayload(
                "Pode me contar com suas palavras o que está acontecendo com o aparelho?",
                new TriagemLlmCamposEstruturados(null, null, null, null), [],
                new TriagemLlmSinalDesvio(Detectado: false, Motivo: null)),
            new TriagemLlmUsageInfo("modelo-teste", 10, 5, TimeSpan.FromMilliseconds(100))));

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, Guid.NewGuid(),
            "+5584994241248", CancellationToken.None);

        var triagem = await fixture.TriagemRepository.FindByIdAsync(fixture.TriagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.EmColeta, triagem!.Status);

        // 2 tentativas registradas (1 falha + 1 sucesso), um único comando enviado.
        Assert.Equal(2, fixture.UsageRepository.Records.Count);
        Assert.Equal(ETriagemLlmOutcome.ProviderUnavailable, fixture.UsageRepository.Records[0].Outcome);
        Assert.Equal(ETriagemLlmOutcome.Success, fixture.UsageRepository.Records[1].Outcome);
        Assert.Single(fixture.CommandRepository.Commands);

        // Backoff de 1s solicitado só antes da 2ª tentativa (que teve sucesso, sem 3ª tentativa).
        Assert.Equal([TimeSpan.FromSeconds(1)], fixture.RetryDelay.RequestedDelays);
    }

    [Fact]
    public async Task ConductAsync_FlagDesligada_NaoChamaALlmNemAlteraEstado()
    {
        var fixture = CreateFixture(optionsFactory: _ => new TriagemLlmConversationOptions
        {
            ConversationEnabled = false,
            AllowedTenantIds = [],
            AllowedPhones = [],
        });
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Success(
            new TriagemLlmSuccessPayload("Resposta que nunca deveria ser usada.",
                new TriagemLlmCamposEstruturados(null, null, null, null), [],
                new TriagemLlmSinalDesvio(false, null)),
            new TriagemLlmUsageInfo("modelo-teste", 1, 1, TimeSpan.Zero)));

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, Guid.NewGuid(),
            "+5584994241248", CancellationToken.None);

        var triagem = await fixture.TriagemRepository.FindByIdAsync(fixture.TriagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.EmColeta, triagem!.Status);
        Assert.Empty(fixture.CommandRepository.Commands);
        Assert.Empty(fixture.UsageRepository.Records);
    }

    [Fact]
    public async Task ConductAsync_TelefoneForaDaAllowlist_NaoChamaALlmNemAlteraEstado()
    {
        var fixture = CreateFixture(optionsFactory: _ => new TriagemLlmConversationOptions
        {
            ConversationEnabled = true,
            AllowedTenantIds = [],
            AllowedPhones = ["+5584994241248"],
        });
        // TenantId não está na allowlist (vazia) mesmo com o telefone
        // correto — AND entre as duas listas (fail-closed).
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Success(
            new TriagemLlmSuccessPayload("Resposta que nunca deveria ser usada.",
                new TriagemLlmCamposEstruturados(null, null, null, null), [],
                new TriagemLlmSinalDesvio(false, null)),
            new TriagemLlmUsageInfo("modelo-teste", 1, 1, TimeSpan.Zero)));

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, Guid.NewGuid(),
            "+5584994241248", CancellationToken.None);

        var triagem = await fixture.TriagemRepository.FindByIdAsync(fixture.TriagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.EmColeta, triagem!.Status);
        Assert.Empty(fixture.CommandRepository.Commands);
        Assert.Empty(fixture.UsageRepository.Records);
    }

    [Fact]
    public async Task ConductAsync_NumeroForaDaAllowlistDeTelefone_NaoChamaALlmNemAlteraEstado()
    {
        var fixture = CreateFixture();

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, Guid.NewGuid(),
            "+5511900000000", CancellationToken.None);

        var triagem = await fixture.TriagemRepository.FindByIdAsync(fixture.TriagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.EmColeta, triagem!.Status);
        Assert.Empty(fixture.CommandRepository.Commands);
        Assert.Empty(fixture.UsageRepository.Records);
    }

    [Fact]
    public async Task ConductAsync_MesmaMensagemInboundReentregue_NaoRepeteAChamadaALlm()
    {
        var fixture = CreateFixture();
        fixture.LlmClient.EnqueueResult(TriagemLlmResult.Success(
            new TriagemLlmSuccessPayload("Pode me contar com suas palavras o que está acontecendo com o aparelho?",
                new TriagemLlmCamposEstruturados(null, null, null, null), [],
                new TriagemLlmSinalDesvio(false, null)),
            new TriagemLlmUsageInfo("modelo-teste", 10, 5, TimeSpan.FromMilliseconds(100))));
        var messageId = Guid.NewGuid();

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, messageId, "+5584994241248",
            CancellationToken.None);
        // Reentrega do mesmo evento inbound (ex.: at-least-once do canal):
        // nenhuma segunda chamada à LLM nem segundo comando.
        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, messageId, "+5584994241248",
            CancellationToken.None);

        Assert.Single(fixture.UsageRepository.Records);
        Assert.Single(fixture.CommandRepository.Commands);
    }

    [Fact]
    public async Task ConductAsync_OrcamentoDeChamadasExcedido_MoveParaAguardandoIntervencaoSemChamarALlm()
    {
        var fixture = CreateFixture(optionsFactory: tenantId => new TriagemLlmConversationOptions
        {
            ConversationEnabled = true,
            AllowedTenantIds = [tenantId],
            AllowedPhones = ["+5584994241248"],
            MaxLlmCallsPerTriagem = 1,
        });
        await fixture.UsageRepository.RecordAsync(
            new TriagemLlmUsageRecord(fixture.TenantId, fixture.TriagemId, "modelo-teste", 10, 5, 100,
                ETriagemLlmOutcome.Success, Now),
            CancellationToken.None);

        await fixture.Service.ConductAsync(fixture.TenantId, fixture.TriagemId, Guid.NewGuid(), "+5584994241248",
            CancellationToken.None);

        var triagem = await fixture.TriagemRepository.FindByIdAsync(fixture.TriagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.AguardandoIntervencao, triagem!.Status);
        Assert.Empty(fixture.CommandRepository.Commands);
        // Nenhum novo registro de uso: o orçamento é checado antes de chamar a LLM.
        Assert.Single(fixture.UsageRepository.Records);
    }
}
