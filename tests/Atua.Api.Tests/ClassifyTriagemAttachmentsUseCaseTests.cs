using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Llm;
using Atua.Api.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de <see cref="ClassifyTriagemAttachmentsUseCase"/> — classificação
/// de visão computacional no Bedrock de anexos de imagem (decisão do PO de
/// adiantar a conferência automática de nota fiscal e etiqueta via
/// WhatsApp, antes do motor JEV completo). Cobre: nota fiscal/etiqueta
/// legíveis marcam o item determinístico do checklist; ilegível não marca;
/// vídeo nunca é enviado ao classificador; imagem acima do limite do
/// Bedrock fica pendente; falha do provedor fica pendente sem quebrar o
/// fluxo; idempotência (mídia já classificada nunca é reclassificada); uso
/// é registrado para toda chamada (inclusive falhas).
/// </summary>
public class ClassifyTriagemAttachmentsUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        ClassifyTriagemAttachmentsUseCase UseCase,
        FakeTriagemAttachmentVisionRepository MediaRepository,
        FakeTriagemInboundMediaStorage Storage,
        FakeTriagemAttachmentVisionClient VisionClient,
        FakeTriagemLlmUsageRepository UsageRepository);

    private static Fixture CreateFixture()
    {
        var mediaRepository = new FakeTriagemAttachmentVisionRepository();
        var storage = new FakeTriagemInboundMediaStorage();
        var visionClient = new FakeTriagemAttachmentVisionClient();
        var usageRepository = new FakeTriagemLlmUsageRepository();

        var useCase = new ClassifyTriagemAttachmentsUseCase(
            mediaRepository,
            storage,
            visionClient,
            usageRepository,
            new FakeTimeProvider(Now),
            NullLogger<ClassifyTriagemAttachmentsUseCase>.Instance);

        return new Fixture(useCase, mediaRepository, storage, visionClient, usageRepository);
    }

    private static Triagem CriarTriagemEmColeta(Guid tenantId, Guid triagemId)
    {
        var triagem = new Triagem(triagemId, tenantId, Guid.NewGuid(), Guid.NewGuid(), Now);
        triagem.MarcarContatoIniciado(Now);
        triagem.IniciarColeta(Now);
        return triagem;
    }

    private static TriagemInboundMedia CriarMidiaImagem(
        Guid tenantId, Guid triagemId, ETriagemInboundMediaKind kind = ETriagemInboundMediaKind.Image,
        string contentType = "image/jpeg", long sizeBytes = 1024) =>
        new(Guid.NewGuid(), tenantId, Guid.NewGuid(), $"wamid-{Guid.NewGuid()}", $"idem-{Guid.NewGuid()}",
            Guid.NewGuid(), "5584999998888", "chat", fromMe: false, Now.AddMinutes(-5),
            kind, contentType, sizeBytes, "sha", isVoiceNote: false, durationSeconds: null,
            caption: null, ETriagemInboundMediaOutcome.Armazenada, triagemId, [],
            $"tenants/{tenantId:D}/inbound-media/{Guid.NewGuid():D}", Now.AddMinutes(-5));

    private static TriagemAttachmentVisionPayload NotaFiscalLegivel() =>
        new(ETriagemAttachmentVisionTipo.NotaFiscal, Legivel: true, Modelo: null, NumeroSerie: null, Confianca: 0.95);

    private static TriagemAttachmentVisionPayload EtiquetaLegivel(string modelo = "Modelo-X", string serie = "SN-998877") =>
        new(ETriagemAttachmentVisionTipo.Etiqueta, Legivel: true, modelo, serie, Confianca: 0.9);

    private static TriagemLlmUsageInfo Usage() => new("modelo-visao-teste", 50, 10, TimeSpan.FromMilliseconds(200));

    [Fact]
    public async Task ClassifyPendingAsync_NotaFiscalLegivel_MarcaItemDoChecklist()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = CriarTriagemEmColeta(tenantId, triagemId);
        var media = CriarMidiaImagem(tenantId, triagemId);
        fixture.MediaRepository.Add(media);
        await fixture.Storage.PutIfAbsentAsync(
            new TriagemInboundMediaStorageKey(tenantId, media.Id),
            new MemoryStream([1, 2, 3]), "image/jpeg", 3, CancellationToken.None);
        fixture.VisionClient.EnqueueResult(TriagemAttachmentVisionResult.Success(NotaFiscalLegivel(), Usage()));

        await fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None);

        Assert.True(triagem.NotaFiscalLegivel);
        Assert.NotNull(media.VisionClassifiedAtUtc);
        Assert.Equal(ETriagemAttachmentVisionTipo.NotaFiscal, media.VisionTipoDetectado);
        Assert.Equal(1, fixture.MediaRepository.SaveChangesCallCount);
        Assert.Single(fixture.UsageRepository.Records);
        Assert.Equal(ETriagemLlmOutcome.Success, fixture.UsageRepository.Records[0].Outcome);
    }

    [Fact]
    public async Task ClassifyPendingAsync_EtiquetaLegivelComModeloESerie_MarcaItemDoChecklist()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = CriarTriagemEmColeta(tenantId, triagemId);
        var media = CriarMidiaImagem(tenantId, triagemId);
        fixture.MediaRepository.Add(media);
        await fixture.Storage.PutIfAbsentAsync(
            new TriagemInboundMediaStorageKey(tenantId, media.Id),
            new MemoryStream([1, 2, 3]), "image/jpeg", 3, CancellationToken.None);
        fixture.VisionClient.EnqueueResult(
            TriagemAttachmentVisionResult.Success(EtiquetaLegivel("Modelo-X", "SN-998877"), Usage()));

        await fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None);

        Assert.Equal("Modelo-X", triagem.EtiquetaModelo);
        Assert.Equal("SN-998877", triagem.EtiquetaSerie);
        Assert.NotNull(media.VisionClassifiedAtUtc);
    }

    [Fact]
    public async Task ClassifyPendingAsync_EtiquetaIlegivel_NaoMarcaItem()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = CriarTriagemEmColeta(tenantId, triagemId);
        var media = CriarMidiaImagem(tenantId, triagemId);
        fixture.MediaRepository.Add(media);
        await fixture.Storage.PutIfAbsentAsync(
            new TriagemInboundMediaStorageKey(tenantId, media.Id),
            new MemoryStream([1, 2, 3]), "image/jpeg", 3, CancellationToken.None);
        fixture.VisionClient.EnqueueResult(TriagemAttachmentVisionResult.Success(
            new TriagemAttachmentVisionPayload(ETriagemAttachmentVisionTipo.Etiqueta, Legivel: false, null, null, 0.4),
            Usage()));

        await fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None);

        Assert.Null(triagem.EtiquetaModelo);
        Assert.Null(triagem.EtiquetaSerie);
        // Ilegível ainda é uma classificação bem-sucedida do provedor — a
        // mídia fica marcada (idempotência) e o fato (ilegível) fica
        // disponível para a LLM conversacional pedir nova foto.
        Assert.NotNull(media.VisionClassifiedAtUtc);
        Assert.False(media.VisionLegivel);
    }

    [Fact]
    public async Task ClassifyPendingAsync_Video_NuncaChamaClassificador()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = CriarTriagemEmColeta(tenantId, triagemId);
        var videoMedia = CriarMidiaImagem(tenantId, triagemId, kind: ETriagemInboundMediaKind.Video, contentType: "video/mp4");
        fixture.MediaRepository.Add(videoMedia);

        await fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None);

        Assert.Empty(fixture.VisionClient.Requests);
        Assert.Null(videoMedia.VisionClassifiedAtUtc);
        Assert.Empty(fixture.UsageRepository.Records);
    }

    [Fact]
    public async Task ClassifyPendingAsync_ImagemAcimaDoLimiteDoBedrock_FicaPendente()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = CriarTriagemEmColeta(tenantId, triagemId);
        var media = CriarMidiaImagem(tenantId, triagemId, sizeBytes: ClassifyTriagemAttachmentsUseCase.MaxImageBytes + 1);
        fixture.MediaRepository.Add(media);

        await fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None);

        Assert.Empty(fixture.VisionClient.Requests);
        Assert.Null(media.VisionClassifiedAtUtc);
        Assert.Empty(fixture.UsageRepository.Records);
    }

    [Fact]
    public async Task ClassifyPendingAsync_FalhaDoProvedor_FicaPendenteSemQuebrarOFluxo()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = CriarTriagemEmColeta(tenantId, triagemId);
        var media = CriarMidiaImagem(tenantId, triagemId);
        fixture.MediaRepository.Add(media);
        await fixture.Storage.PutIfAbsentAsync(
            new TriagemInboundMediaStorageKey(tenantId, media.Id),
            new MemoryStream([1, 2, 3]), "image/jpeg", 3, CancellationToken.None);
        fixture.VisionClient.EnqueueResult(TriagemAttachmentVisionResult.Unavailable("timeout simulado"));

        var exception = await Record.ExceptionAsync(
            () => fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None));

        Assert.Null(exception);
        Assert.Null(media.VisionClassifiedAtUtc);
        Assert.False(triagem.NotaFiscalLegivel);
        Assert.Single(fixture.UsageRepository.Records);
        Assert.Equal(ETriagemLlmOutcome.ProviderUnavailable, fixture.UsageRepository.Records[0].Outcome);
    }

    [Fact]
    public async Task ClassifyPendingAsync_FalhaDeLeituraDoStorage_FicaPendenteSemQuebrarOFluxo()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = CriarTriagemEmColeta(tenantId, triagemId);
        var media = CriarMidiaImagem(tenantId, triagemId);
        fixture.MediaRepository.Add(media);
        var key = new TriagemInboundMediaStorageKey(tenantId, media.Id);
        await fixture.Storage.PutIfAbsentAsync(key, new MemoryStream([1, 2, 3]), "image/jpeg", 3, CancellationToken.None);
        fixture.Storage.FailGetForKeys.Add(key.Value);

        var exception = await Record.ExceptionAsync(
            () => fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None));

        Assert.Null(exception);
        Assert.Null(media.VisionClassifiedAtUtc);
        Assert.Empty(fixture.VisionClient.Requests);
        Assert.Empty(fixture.UsageRepository.Records);
    }

    [Fact]
    public async Task ClassifyPendingAsync_MidiaJaClassificada_NuncaReclassifica()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = CriarTriagemEmColeta(tenantId, triagemId);
        var media = CriarMidiaImagem(tenantId, triagemId);
        media.RegistrarClassificacaoVisao(ETriagemAttachmentVisionTipo.NotaFiscal, legivel: true, null, null, 0.9, Now);
        fixture.MediaRepository.Add(media);

        await fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None);

        Assert.Empty(fixture.VisionClient.Requests);
        Assert.True(triagem.NotaFiscalLegivel);
        Assert.Equal(0, fixture.MediaRepository.SaveChangesCallCount);
    }

    [Fact]
    public async Task ClassifyPendingAsync_RegraDeterministicaReaplicadaReaplicada_NaoChurnaConcorrenciaQuandoJaSatisfeita()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = CriarTriagemEmColeta(tenantId, triagemId);
        var media = CriarMidiaImagem(tenantId, triagemId);
        media.RegistrarClassificacaoVisao(ETriagemAttachmentVisionTipo.NotaFiscal, legivel: true, null, null, 0.9, Now);
        fixture.MediaRepository.Add(media);

        await fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None);
        var tokenAfterFirstCall = triagem.ConcurrencyToken;

        await fixture.UseCase.ClassifyPendingAsync(tenantId, triagem, CancellationToken.None);

        Assert.Equal(tokenAfterFirstCall, triagem.ConcurrencyToken);
    }
}
