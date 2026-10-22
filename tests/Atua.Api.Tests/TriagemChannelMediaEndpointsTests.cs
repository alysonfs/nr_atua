using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Atua.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Atua.Api.Tests;

/// <summary>
/// Testes HTTP de <c>POST /api/internal/triagens/canal/midias</c> — escopo
/// exclusivo <c>channel.media.ingest</c>, multipart/form-data com as partes
/// <c>metadata</c> (JSON do envelope) e <c>file</c> (binário). Usa
/// <see cref="FakeTriagemInboundMediaStorage"/> no lugar do S3 real — nenhum
/// teste aqui faz qualquer chamada à AWS.
///
/// Os arquivos de teste usam buffers sintéticos com assinaturas mínimas de
/// PDF, OGG e MP4 para validar o contrato sem depender de arquivos reais.
/// </summary>
public class TriagemChannelMediaEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private const string SenderPhone = "5511999991234";

    [Fact]
    public async Task IngestMedia_UmaTriagemEmContatoIniciado_ArmazenaEIniciaColeta()
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(CreatePdfMetadata("media-1", "idem-media-1")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.NotNull(body);
        Assert.Equal("ColetaIniciada", body!.Outcome);
        Assert.Equal(seed.TriagemId, body.TriagemId);
        Assert.Equal(1, body.CandidateCount);

        await using var context = CreateContext(databaseName);
        Assert.Equal(ETriagemStatus.EmColeta,
            (await context.Triagens.SingleAsync(t => t.Id == seed.TriagemId)).Status);
        var stored = await context.TriagemInboundMedia.SingleAsync();
        Assert.Equal(seed.TriagemId, stored.TriagemId);
        Assert.NotNull(stored.StorageKey);
        Assert.Equal($"tenants/{seed.TenantId:D}/inbound-media/{stored.Id:D}", stored.StorageKey);
        Assert.Single(storage.Objects);
        Assert.Equal(stored.ExpiresAtUtc, stored.ReceivedAtUtc.AddYears(5));
    }

    [Theory]
    [InlineData("document", "application/pdf")]
    [InlineData("audio", "audio/ogg")]
    [InlineData("video", "video/mp4")]
    public async Task IngestMedia_FormatoAceito_Armazena(
        string mediaKind,
        string contentType)
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);
        var bytes = BuildSyntheticMediaBytes(contentType);
        var metadata = CreatePdfMetadata($"media-{mediaKind}", $"idem-{mediaKind}") with
        {
            MediaKind = mediaKind,
            ContentType = contentType,
            SizeBytes = bytes.LongLength,
            Sha256 = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(bytes)),
            IsVoiceNote = mediaKind == "audio",
            DurationSeconds = mediaKind == "audio" ? 2 : null,
        };

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(metadata, bytes));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.NotNull(body);
        Assert.Equal("ColetaIniciada", body!.Outcome);
        Assert.Equal(seed.TriagemId, body.TriagemId);
        Assert.Single(storage.Objects);

        await using var context = CreateContext(databaseName);
        var stored = await context.TriagemInboundMedia.SingleAsync();
        Assert.Equal(contentType, stored.ContentType);
        Assert.Equal(bytes.LongLength, stored.SizeBytes);
        Assert.NotNull(stored.StorageKey);
    }

    [Fact]
    public async Task IngestMedia_TriagemForaDeContatoIniciadoOuEmColeta_ArmazenaSemTransicionar()
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        await using (var seedContext = CreateContext(databaseName))
        {
            var triagem = await seedContext.Triagens.SingleAsync(t => t.Id == seed.TriagemId);
            triagem.IniciarColeta(Now);
            triagem.Expirar(Now);
            await seedContext.SaveChangesAsync();
        }

        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(CreatePdfMetadata("media-finished", "idem-media-finished")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.NotNull(body);
        Assert.Equal("Armazenada", body!.Outcome);
        Assert.Equal(seed.TriagemId, body.TriagemId);

        await using var context = CreateContext(databaseName);
        Assert.Equal(ETriagemStatus.Expirada,
            (await context.Triagens.SingleAsync(t => t.Id == seed.TriagemId)).Status);
        Assert.Single(storage.Objects);
    }

    [Fact]
    public async Task IngestMedia_SemTriagemCorrespondente_NaoArmazenaBinario()
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999990000");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(CreatePdfMetadata("media-no-match", "idem-no-match")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.NotNull(body);
        Assert.Equal("SemTriagem", body!.Outcome);
        Assert.Null(body.TriagemId);
        Assert.Empty(storage.Objects);

        await using var context = CreateContext(databaseName);
        var stored = await context.TriagemInboundMedia.SingleAsync();
        Assert.Null(stored.StorageKey);
        Assert.NotNull(stored.Sha256);
    }

    [Fact]
    public async Task IngestMedia_TriagensAmbiguas_NaoArmazenaBinarioNemAssocia()
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var first = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        await SeedContactStartedTriageAsync(
            databaseName, "55 (11) 99999-1234", first.TenantId, first.IntegrationId);
        var client = CreateClient(app, first.TenantId, first.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(CreatePdfMetadata("media-ambiguous", "idem-ambiguous")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.NotNull(body);
        Assert.Equal("Ambigua", body!.Outcome);
        Assert.Null(body.TriagemId);
        Assert.Equal(2, body.CandidateCount);
        Assert.Empty(storage.Objects);
    }

    [Theory]
    [InlineData("group", false, "IgnoradaGrupo")]
    [InlineData("individual", true, "IgnoradaPropria")]
    public async Task IngestMedia_EventoNaoAceito_NaoArmazenaBinario(
        string conversationKind, bool fromMe, string expectedOutcome)
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);
        var metadata = CreatePdfMetadata($"media-{expectedOutcome}", $"idem-{expectedOutcome}") with
        {
            ConversationKind = conversationKind,
            FromMe = fromMe,
        };

        var response = await client.PostAsync("/api/internal/triagens/canal/midias", BuildMultipartContent(metadata));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.NotNull(body);
        Assert.Equal(expectedOutcome, body!.Outcome);
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task IngestMedia_TipoNaoCorrespondeAssinaturaBinaria_RejeitaSemArmazenar()
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);
        // Declara PDF mas o conteúdo não começa com a assinatura "%PDF-".
        var fakeBytes = Encoding.ASCII.GetBytes("isso nao e um pdf valido, apenas texto qualquer");
        var metadata = CreatePdfMetadata("media-bad-signature", "idem-bad-signature") with
        {
            SizeBytes = fakeBytes.Length,
        };

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(metadata, fakeBytes));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.NotNull(body);
        Assert.Equal("RejeitadaTipo", body!.Outcome);
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task IngestMedia_TamanhoExcedeLimite_RejeitaSemArmazenar()
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);
        var oversized = BuildPdfBytes(IngestTriagemChannelMediaUseCase.MaxContentLengthBytes + 1);
        var metadata = CreatePdfMetadata("media-oversized", "idem-oversized") with
        {
            SizeBytes = oversized.Length,
        };

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(metadata, oversized));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.NotNull(body);
        Assert.Equal("RejeitadaTamanho", body!.Outcome);
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task IngestMedia_ReentregaDaMesmaChaveComMesmoConteudo_NaoDuplicaNemRearmazena()
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);
        var metadata = CreatePdfMetadata("media-replay", "idem-replay");

        var first = await client.PostAsync("/api/internal/triagens/canal/midias", BuildMultipartContent(metadata));
        var second = await client.PostAsync("/api/internal/triagens/canal/midias", BuildMultipartContent(metadata));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.Equal(firstBody!.Outcome, secondBody!.Outcome);
        Assert.Equal(firstBody.TriagemId, secondBody.TriagemId);

        await using var context = CreateContext(databaseName);
        Assert.Equal(1, await context.TriagemInboundMedia.CountAsync());
        Assert.Single(storage.Objects);
    }

    [Fact]
    public async Task IngestMedia_ReentregaDaMesmaChaveComConteudoDiferente_Retorna409()
    {
        var (app, databaseName, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);
        var metadata = CreatePdfMetadata("media-conflict", "idem-conflict");
        var first = await client.PostAsync("/api/internal/triagens/canal/midias", BuildMultipartContent(metadata));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var conflictingContent = MinimalPdfBytes.ToArray();
        conflictingContent[^1] = (byte)'1';
        var conflicting = metadata with
        {
            Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(conflictingContent)),
        };

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(conflicting, conflictingContent));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task IngestMedia_ClaimsDeTenantOuIntegracaoAusentes_Retorna401()
    {
        var (app, _, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(
            "Test-Scope", ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(CreatePdfMetadata("media-no-claims", "idem-no-claims")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IngestMedia_EscopoDeMensagemNaoAutorizaIngestaoDeMidia()
    {
        var (app, databaseName, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(CreatePdfMetadata("media-wrong-scope", "idem-wrong-scope")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IngestMedia_EnvelopeInvalido_Retorna400()
    {
        var (app, databaseName, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);
        var invalidMetadata = CreatePdfMetadata("media-invalid", "idem-invalid") with
        {
            MediaKind = "sticker",
            SenderPhone = "abc",
        };

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(invalidMetadata));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IngestMedia_SemParteFile_Retorna400()
    {
        var (app, databaseName, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(CreatePdfMetadata("media-no-file", "idem-no-file"))),
                "metadata" },
        };

        var response = await client.PostAsync("/api/internal/triagens/canal/midias", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IngestMedia_DocumentoDiferenteDePdf_RejeitaTecnicamente()
    {
        var (app, databaseName, storage) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope);
        var docxBytes = Encoding.ASCII.GetBytes("PK\u0003\u0004 fake docx bytes padding padding");
        var metadata = CreatePdfMetadata("media-docx", "idem-docx") with
        {
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            SizeBytes = docxBytes.Length,
        };

        var response = await client.PostAsync("/api/internal/triagens/canal/midias",
            BuildMultipartContent(metadata, docxBytes));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMediaResponse>();
        Assert.NotNull(body);
        Assert.Equal("RejeitadaTipo", body!.Outcome);
        Assert.Empty(storage.Objects);
    }

    // -------------------------------------------------------------------
    // Infraestrutura de teste
    // -------------------------------------------------------------------

    private static async Task<(WebApplication App, string DatabaseName, FakeTriagemInboundMediaStorage Storage)>
        CreateApplicationAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var capturedDatabaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AtuaDbContext>(options =>
            options.UseInMemoryDatabase(capturedDatabaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        var storage = new FakeTriagemInboundMediaStorage();
        builder.Services.AddSingleton<ITriagemInboundMediaStorage>(storage);
        builder.Services.AddScoped<ITriagemInboundMediaRepository, TriagemInboundMediaRepository>();
        builder.Services.AddScoped<IIngestTriagemChannelMediaUseCase, IngestTriagemChannelMediaUseCase>();
        // Os demais endpoints do mesmo grupo (/claim, /complete, /mensagens) são
        // inspecionados pelo ASP.NET ao construir o EndpointDataSource — mesmo
        // sem serem exercitados por estes testes, seus parâmetros precisam ser
        // resolvíveis via DI, senão a inferência de metadados falha para todo o
        // grupo. Registra as mesmas dependências usadas em
        // TriagemChannelEndpointsTests.
        builder.Services.AddScoped<ITriagemRepository, TriagemRepository>();
        builder.Services.AddScoped<ITriagemChannelCommandRepository, TriagemChannelCommandRepository>();
        builder.Services.AddScoped<TriagemChannelCommandService>();
        builder.Services.AddScoped<ITriagemInboundMessageRepository, TriagemInboundMessageRepository>();
        builder.Services.AddScoped<IIngestTriagemChannelMessageUseCase, IngestTriagemChannelMessageUseCase>();
        builder.Services.AddSingleton(
            Microsoft.Extensions.Options.Options.Create(new TriagemChannelOptions { LeaseSeconds = 60 }));
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("ChannelCommandClaim", policy =>
                policy.AddAuthenticationSchemes("Test")
                    .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope));
            options.AddPolicy("ChannelCommandComplete", policy =>
                policy.AddAuthenticationSchemes("Test")
                    .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope));
            options.AddPolicy("ChannelMessageIngest", policy =>
                policy.AddAuthenticationSchemes("Test")
                    .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope));
            options.AddPolicy("ChannelMediaIngest", policy =>
                policy.AddAuthenticationSchemes("Test")
                    .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope));
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapTriagemChannelEndpoints();
        await app.StartAsync();

        return (app, capturedDatabaseName, storage);
    }

    private static HttpClient CreateClient(WebApplication app, Guid tenantId, Guid integrationId, string scope)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-TenantId", tenantId.ToString());
        client.DefaultRequestHeaders.Add("Test-IntegrationId", integrationId.ToString());
        client.DefaultRequestHeaders.Add("Test-Scope", scope);
        return client;
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid TriagemId, Guid WorkOrderId);

    private static async Task<Seed> SeedContactStartedTriageAsync(
        string databaseName,
        string phone,
        Guid? tenantId = null,
        Guid? integrationId = null)
    {
        await using var context = CreateContext(databaseName);
        var actualTenantId = tenantId ?? Guid.NewGuid();
        var actualIntegrationId = integrationId ?? Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var triagem = new Triagem(triagemId, actualTenantId, actualIntegrationId, workOrderId, Now);
        triagem.MarcarContatoIniciado(Now);
        var key = new TriagemIdempotencyKey(actualTenantId, actualIntegrationId, $"OS-{triagemId:N}");
        var command = new TriagemChannelCommand(
            Guid.NewGuid(),
            actualTenantId,
            actualIntegrationId,
            triagemId,
            workOrderId,
            "template-version-id",
            1,
            "conteúdo renderizado",
            $"command-{triagemId:N}",
            Guid.NewGuid(),
            Now,
            recipientPhone: phone);
        context.Add(triagem);
        context.Entry(triagem)
            .Property(TriagemPersistenceSchema.WorkOrderProviderIdShadowPropertyName)
            .CurrentValue = key.WorkOrderProviderId;
        context.Add(command);
        await context.SaveChangesAsync();
        return new Seed(actualTenantId, actualIntegrationId, triagemId, workOrderId);
    }

    /// <summary>Metadata válida (PDF mínimo, 16 bytes) para os cenários felizes — contrato do evento de mídia.</summary>
    private static IngestTriagemChannelMediaRequest CreatePdfMetadata(string messageId, string idempotencyKey) =>
        new(
            "1",
            messageId,
            idempotencyKey,
            Guid.NewGuid(),
            SenderPhone,
            "individual",
            false,
            Now,
            "document",
            "application/pdf",
            MinimalPdfBytes.Length,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(MinimalPdfBytes)),
            false,
            null,
            "segue o comprovante em anexo");

    private static readonly byte[] MinimalPdfBytes = BuildPdfBytes(16);

    private static byte[] BuildPdfBytes(long size)
    {
        var header = "%PDF-1.4\n"u8.ToArray();
        if (size <= header.Length)
        {
            return header[..(int)size];
        }

        var bytes = new byte[size];
        header.CopyTo(bytes, 0);
        Array.Fill(bytes, (byte)'0', header.Length, (int)(size - header.Length));
        return bytes;
    }

    private static byte[] BuildSyntheticMediaBytes(string contentType) => contentType switch
    {
        "application/pdf" => BuildPdfBytes(16),
        "audio/ogg" => Encoding.ASCII.GetBytes("OggS\0\0\0\0"),
        "video/mp4" => [0, 0, 0, 0, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'],
        _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Tipo de mídia sintética não suportado."),
    };

    private static MultipartFormDataContent BuildMultipartContent(
        IngestTriagemChannelMediaRequest metadata, byte[]? fileContent = null)
    {
        var bytes = fileContent ?? BuildPdfBytes(metadata.SizeBytes ?? MinimalPdfBytes.Length);
        var fileContentPart = new ByteArrayContent(bytes);
        fileContentPart.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(metadata.ContentType ?? "application/pdf");
        return new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(metadata)), "metadata" },
            { fileContentPart, "file", "inbound-media.bin" },
        };
    }

    /// <summary>
    /// Simula os claims produzidos por <see cref="ServiceCredentialAuthenticationHandler"/>
    /// a partir de headers de teste, sem depender de um banco real de
    /// credenciais.
    /// </summary>
    private sealed class TestAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>();
            if (Request.Headers.TryGetValue("Test-TenantId", out var tenantId))
            {
                claims.Add(new Claim("tenant_id", tenantId.ToString()));
            }

            if (Request.Headers.TryGetValue("Test-IntegrationId", out var integrationId))
            {
                claims.Add(new Claim("integration_id", integrationId.ToString()));
            }

            if (Request.Headers.TryGetValue("Test-Scope", out var scope))
            {
                claims.Add(new Claim("scope", scope.ToString()));
            }

            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }
}
