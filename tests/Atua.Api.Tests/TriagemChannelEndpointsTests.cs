using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Llm;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

/// <summary>
/// Testes HTTP de <c>POST /api/internal/triagens/canal/claim</c> e
/// <c>/complete</c> (ADR-042). Usa <see cref="TestServer"/> com EF InMemory
/// e um esquema de autenticação de teste que simula os claims produzidos
/// por <see cref="ServiceCredentialAuthenticationHandler"/>, cobrindo
/// autorização por escopo exclusivo, isolamento tenant+integração e
/// ProblemDetails.
///
/// Limitação reconhecida (mesma de <see cref="TriagemChannelCommandClaimSqlTests"/>):
/// o provider EF Core InMemory não executa SQL bruto (<c>FromSqlRaw</c>),
/// usado apenas por <c>TriagemChannelCommandRepository.ClaimNextAsync</c>.
/// Por isso, os testes de <c>/claim</c> usam <see cref="FakeTriagemChannelCommandRepository"/>
/// no lugar do repositório real — cobrindo roteamento, autorização e forma
/// do DTO, não a concorrência real de <c>FOR UPDATE SKIP LOCKED</c> (já
/// coberta estruturalmente por <c>TriagemChannelCommandClaimSqlTests</c> e
/// comportamentalmente por <c>TriagemChannelCommandServiceTests</c>, ambos
/// com o mesmo fake). Os testes de <c>/complete</c> usam o repositório real
/// <see cref="TriagemChannelCommandRepository"/> (apenas EF/LINQ padrão,
/// sem SQL bruto — suportado pelo InMemory), seedando o comando já
/// <c>Claimed</c> diretamente via <see cref="AtuaDbContext"/> para evitar
/// passar pelo claim real.
/// </summary>
public class TriagemChannelEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    // -------------------------------------------------------------------
    // Claim
    // -------------------------------------------------------------------

    [Fact]
    public async Task Claim_ComComandoPendente_Retorna200ComPayload()
    {
        var (app, databaseName, commandRepository) = await CreateApplicationWithFakeCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedPendingCommandAsync(databaseName, commandRepository);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/claim", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClaimChannelCommandResponse>();
        Assert.NotNull(body);
        Assert.Equal(seed.CommandId, body!.CommandId);
        Assert.Equal(seed.TriagemId, body.TriagemId);
        Assert.Equal(seed.WorkOrderId, body.WorkOrderId);
        Assert.Equal("template-version-id", body.TemplateId);
        Assert.Equal(1, body.TemplateVersion);
        Assert.Equal("conteúdo renderizado", body.RenderedContent);
        Assert.Equal("+5511999999999", body.RecipientPhone);
    }

    [Fact]
    public async Task Claim_SemComandoElegivel_Retorna204()
    {
        var (app, _, _) = await CreateApplicationWithFakeCommandRepositoryAsync();
        await using var appDisposable = app;
        var client = CreateClient(app, Guid.NewGuid(), Guid.NewGuid(),
            ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/claim", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Claim_NuncaReivindicaComandoDeOutraTenantOuIntegracao()
    {
        var (app, databaseName, commandRepository) = await CreateApplicationWithFakeCommandRepositoryAsync();
        await using var appDisposable = app;
        await SeedPendingCommandAsync(databaseName, commandRepository);
        var client = CreateClient(app, Guid.NewGuid(), Guid.NewGuid(),
            ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/claim", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Claim_ClaimsDeTenantOuIntegracaoAusentes_Retorna401()
    {
        var (app, _, _) = await CreateApplicationWithFakeCommandRepositoryAsync();
        await using var appDisposable = app;
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-Scope", ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/claim", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Claim_EscopoCompleteNaoAutorizaClaim()
    {
        var (app, databaseName, commandRepository) = await CreateApplicationWithFakeCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedPendingCommandAsync(databaseName, commandRepository);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsync("/api/internal/triagens/canal/claim", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -------------------------------------------------------------------
    // Complete
    // -------------------------------------------------------------------

    [Fact]
    public async Task Complete_Success_Retorna200EPersisteTriagemComoContatoIniciado()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete",
            new CompleteChannelCommandRequest(seed.CommandId, Now, "Success", null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompleteChannelCommandResponse>();
        Assert.NotNull(body);
        Assert.Equal("Completed", body!.Status);

        await using var context = CreateContext(databaseName);
        var triagem = await context.Triagens.SingleAsync(t => t.Id == seed.TriagemId);
        Assert.Equal(ETriagemStatus.ContatoIniciado, triagem.Status);
    }

    [Fact]
    public async Task Complete_PermanentFailure_PersisteTriagemComoFalhaDeContato()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete",
            new CompleteChannelCommandRequest(seed.CommandId, Now, "PermanentFailure", "numero-invalido",
                "Número de contato inválido."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompleteChannelCommandResponse>();
        Assert.Equal("Failed", body!.Status);

        await using var context = CreateContext(databaseName);
        var triagem = await context.Triagens.SingleAsync(t => t.Id == seed.TriagemId);
        Assert.Equal(ETriagemStatus.FalhaDeContato, triagem.Status);
    }

    [Fact]
    public async Task Complete_TransientFailure_VoltaParaPendingSemAlterarTriagem()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete",
            new CompleteChannelCommandRequest(seed.CommandId, Now, "TransientFailure", "timeout-provedor",
                "Tempo esgotado."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompleteChannelCommandResponse>();
        Assert.Equal("Pending", body!.Status);

        await using var context = CreateContext(databaseName);
        var triagem = await context.Triagens.SingleAsync(t => t.Id == seed.TriagemId);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, triagem.Status);
        var command = await context.TriagemChannelCommands.SingleAsync(c => c.Id == seed.CommandId);
        Assert.Equal(1, command.AttemptCount);
    }

    [Fact]
    public async Task Complete_ReentregaDeComandoJaConcluido_EhIdempotenteERetornaOMesmoResultado()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);
        var request = new CompleteChannelCommandRequest(seed.CommandId, Now, "Success", null, null);

        var first = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete", request);
        var second = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete", request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<CompleteChannelCommandResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<CompleteChannelCommandResponse>();
        Assert.Equal(firstBody!.Status, secondBody!.Status);
        Assert.Equal(firstBody.CompletedAtUtc, secondBody.CompletedAtUtc);
    }

    [Fact]
    public async Task Complete_OutcomeInvalido_Retorna400ValidationProblem()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete",
            new CompleteChannelCommandRequest(seed.CommandId, Now, "outcome-invalido", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Complete_CommandIdInexistente_Retorna404()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete",
            new CompleteChannelCommandRequest(Guid.NewGuid(), Now, "Success", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Complete_ComandoDeOutraTenant_Retorna404()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var client = CreateClient(app, Guid.NewGuid(), seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete",
            new CompleteChannelCommandRequest(seed.CommandId, Now, "Success", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Complete_EscopoClaimNaoAutorizaComplete()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete",
            new CompleteChannelCommandRequest(seed.CommandId, Now, "Success", null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -------------------------------------------------------------------
    // Lease perdida (ADR-042, correção de concorrência)
    // -------------------------------------------------------------------

    [Fact]
    public async Task Complete_TokenDeLeaseDiferenteDoVigente_Retorna409SemAlterarComandoOuTriagem()
    {
        // Claim A expira e é substituído por claim B (nova lease, novo
        // token). Complete apresentando o token antigo (A) nunca deve
        // aplicar efeito.
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var tokenA = Now;
        var tokenB = Now.AddSeconds(90);
        await ReclaimCommandAsync(databaseName, seed.CommandId, tokenB);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete",
            new CompleteChannelCommandRequest(seed.CommandId, tokenA, "Success", null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var context = CreateContext(databaseName);
        var triagem = await context.Triagens.SingleAsync(t => t.Id == seed.TriagemId);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, triagem.Status);
        var command = await context.TriagemChannelCommands.SingleAsync(c => c.Id == seed.CommandId);
        Assert.Equal(ETriagemChannelCommandStatus.Claimed, command.Status);
        Assert.Equal(tokenB, command.ClaimedAtUtc);
    }

    [Fact]
    public async Task Complete_AposLeaseSubstituida_TitularVigenteConcluiComSucesso()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedClaimedCommandAsync(databaseName);
        var tokenB = Now.AddSeconds(90);
        await ReclaimCommandAsync(databaseName, seed.CommandId, tokenB);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/complete",
            new CompleteChannelCommandRequest(seed.CommandId, tokenB, "Success", null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompleteChannelCommandResponse>();
        Assert.Equal("Completed", body!.Status);

        await using var context = CreateContext(databaseName);
        var triagem = await context.Triagens.SingleAsync(t => t.Id == seed.TriagemId);
        Assert.Equal(ETriagemStatus.ContatoIniciado, triagem.Status);
    }

    [Fact]
    public async Task IngestMessage_UmaTriagemCorrespondente_IniciaColetaERegistraEvento()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens",
            CreateInboundMessage("message-accepted", "stable-accepted-key"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMessageResponse>();
        Assert.NotNull(body);
        Assert.Equal("ColetaIniciada", body!.Outcome);
        Assert.Equal(seed.TriagemId, body.TriagemId);
        Assert.Equal(1, body.CandidateCount);

        await using var context = CreateContext(databaseName);
        Assert.Equal(ETriagemStatus.EmColeta,
            (await context.Triagens.SingleAsync(triagem => triagem.Id == seed.TriagemId)).Status);
        var storedMessage = await context.TriagemInboundMessages.SingleAsync();
        Assert.Equal(seed.TriagemId, storedMessage.TriagemId);
    }

    [Theory]
    [InlineData("group", false, "text", "mensagem", "IgnoradaGrupo")]
    [InlineData("individual", true, "text", "mensagem", "IgnoradaPropria")]
    [InlineData("individual", false, "empty", null, "IgnoradaVazia")]
    [InlineData("individual", false, "text", "   ", "IgnoradaVazia")]
    public async Task IngestMessage_EventoNaoAceito_PersisteSemAssociarOuTransicionar(
        string conversationKind,
        bool fromMe,
        string kind,
        string? text,
        string expectedOutcome)
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope);
        var request = CreateInboundMessage("message-ignored", $"ignored-{expectedOutcome}") with
        {
            ConversationKind = conversationKind,
            FromMe = fromMe,
            Kind = kind,
            Text = text,
        };

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMessageResponse>();
        Assert.NotNull(body);
        Assert.Equal(expectedOutcome, body!.Outcome);
        Assert.Null(body.TriagemId);
        Assert.Equal(0, body.CandidateCount);
        await using var context = CreateContext(databaseName);
        Assert.Equal(ETriagemStatus.ContatoIniciado,
            (await context.Triagens.SingleAsync(triagem => triagem.Id == seed.TriagemId)).Status);
        var storedMessage = await context.TriagemInboundMessages.SingleAsync();
        Assert.Null(storedMessage.TriagemId);
        Assert.Empty(storedMessage.CandidateTriagemIds);
    }

    [Fact]
    public async Task IngestMessage_TriagemJaEmColeta_RegistraSemNovaTransicao()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        await using (var context = CreateContext(databaseName))
        {
            var triagem = await context.Triagens.SingleAsync(item => item.Id == seed.TriagemId);
            triagem.IniciarColeta(Now);
            await context.SaveChangesAsync();
        }

        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope);
        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens",
            CreateInboundMessage("message-already-collecting", "stable-already-collecting-key"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMessageResponse>();
        Assert.NotNull(body);
        Assert.Equal("JaEmColeta", body!.Outcome);
        Assert.Equal(seed.TriagemId, body.TriagemId);
        await using var verifyContext = CreateContext(databaseName);
        Assert.Equal(ETriagemStatus.EmColeta,
            (await verifyContext.Triagens.SingleAsync(item => item.Id == seed.TriagemId)).Status);
    }

    [Theory]
    [InlineData("+5584994241248", "558494241248")]
    [InlineData("+558494241248", "5584994241248")]
    public async Task IngestMessage_CelularBrasileiroComOuSemNonoDigito_IniciaColeta(
        string recipientPhone,
        string senderPhone)
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, recipientPhone);
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope);
        var request = CreateInboundMessage("message-ninth-digit", "stable-ninth-digit-key") with
        {
            SenderPhone = senderPhone,
        };

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMessageResponse>();
        Assert.NotNull(body);
        Assert.Equal("ColetaIniciada", body!.Outcome);
        Assert.Equal(seed.TriagemId, body.TriagemId);
        await using var context = CreateContext(databaseName);
        Assert.Equal(senderPhone, (await context.TriagemInboundMessages.SingleAsync()).SenderPhone);
    }

    [Theory]
    [InlineData("5584994241248", "558494241248")]
    [InlineData("558494241248", "558494241248")]
    [InlineData("+55 (84) 99424-1248", "558494241248")]
    [InlineData("558432221234", "558432221234")]
    [InlineData("5511999991234", "551199991234")]
    [InlineData("14155550123", "14155550123")]
    public void ToPhoneMatchKey_RemoveSomenteNonoDigitoDeCelularBrasileiro(string phone, string expected) =>
        Assert.Equal(expected, TriagemInboundMessageRepository.ToPhoneMatchKey(phone));

    [Fact]
    public async Task IngestMessage_SemTriagemCorrespondente_PersisteSemVinculo()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999990000");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens",
            CreateInboundMessage("message-no-match", "stable-no-match-key"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMessageResponse>();
        Assert.NotNull(body);
        Assert.Equal("SemTriagem", body!.Outcome);
        Assert.Null(body.TriagemId);
        Assert.Equal(0, body.CandidateCount);
        await using var context = CreateContext(databaseName);
        Assert.Equal(ETriagemStatus.ContatoIniciado,
            (await context.Triagens.SingleAsync(item => item.Id == seed.TriagemId)).Status);
        Assert.Null((await context.TriagemInboundMessages.SingleAsync()).TriagemId);
    }

    [Fact]
    public async Task IngestMessage_ReentregaDaMesmaChave_NaoRepeteTransicao()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope);
        var request = CreateInboundMessage("message-accepted", "stable-accepted-key");

        var firstResponse = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens", request);
        var secondResponse = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens", request);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<IngestTriagemChannelMessageResponse>();
        Assert.Equal("ColetaIniciada", secondBody!.Outcome);
        await using var context = CreateContext(databaseName);
        Assert.Single(await context.TriagemInboundMessages.ToListAsync());
        Assert.Equal(ETriagemStatus.EmColeta,
            (await context.Triagens.SingleAsync(triagem => triagem.Id == seed.TriagemId)).Status);
    }

    [Fact]
    public async Task IngestMessage_TriagensAmbiguas_PersisteCandidatasSemAssociarOuTransicionar()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var first = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        await SeedContactStartedTriageAsync(databaseName, "55 (11) 99999-1234", first.TenantId, first.IntegrationId);
        var client = CreateClient(app, first.TenantId, first.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens",
            CreateInboundMessage("message-ambiguous", "stable-ambiguous-key"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestTriagemChannelMessageResponse>();
        Assert.NotNull(body);
        Assert.Equal("Ambigua", body!.Outcome);
        Assert.Null(body.TriagemId);
        Assert.Equal(2, body.CandidateCount);
        await using var context = CreateContext(databaseName);
        Assert.All(await context.Triagens.ToListAsync(), triagem =>
            Assert.Equal(ETriagemStatus.ContatoIniciado, triagem.Status));
        var storedMessage = await context.TriagemInboundMessages.SingleAsync();
        Assert.Null(storedMessage.TriagemId);
        Assert.Equal(2, storedMessage.CandidateTriagemIds.Length);
    }

    [Fact]
    public async Task IngestMessage_EscopoDeCompleteNaoAutorizaIngestao()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens",
            CreateInboundMessage("message-forbidden", "stable-forbidden-key"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IngestMessage_EnvelopeInvalido_Retorna400()
    {
        var (app, databaseName) = await CreateApplicationWithRealCommandRepositoryAsync();
        await using var appDisposable = app;
        var seed = await SeedContactStartedTriageAsync(databaseName, "+5511999991234");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope);

        var request = CreateInboundMessage("message-invalid", "stable-invalid-key") with
        {
            ContractVersion = "2",
        };
        var response = await client.PostAsJsonAsync("/api/internal/triagens/canal/mensagens", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    // -------------------------------------------------------------------

    private static async Task<(WebApplication App, string DatabaseName, FakeTriagemChannelCommandRepository CommandRepository)>
        CreateApplicationWithFakeCommandRepositoryAsync()
    {
        var commandRepository = new FakeTriagemChannelCommandRepository();
        var (app, databaseName) = await CreateApplicationAsync(services =>
            services.AddSingleton<ITriagemChannelCommandRepository>(commandRepository));
        return (app, databaseName, commandRepository);
    }

    private static async Task<(WebApplication App, string DatabaseName)> CreateApplicationWithRealCommandRepositoryAsync()
    {
        return await CreateApplicationAsync(services =>
            services.AddScoped<ITriagemChannelCommandRepository, TriagemChannelCommandRepository>());
    }

    private static async Task<(WebApplication App, string DatabaseName)> CreateApplicationAsync(
        Action<IServiceCollection> configureCommandRepository)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var capturedDatabaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AtuaDbContext>(options =>
            options.UseInMemoryDatabase(capturedDatabaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        builder.Services.AddScoped<ITriagemRepository, TriagemRepository>();
        configureCommandRepository(builder.Services);
        builder.Services.AddScoped<TriagemChannelCommandService>();
        builder.Services.AddScoped<ITriagemInboundMessageRepository, TriagemInboundMessageRepository>();
        builder.Services.AddScoped<IIngestTriagemChannelMessageUseCase, IngestTriagemChannelMessageUseCase>();

        // Plano M2 — loop conversacional da LLM: registrado, mas inerte
        // nestes testes (ConversationEnabled=false por padrão / allowlists
        // vazias), já que exercitam apenas o transporte do canal.
        builder.Services.AddScoped<TriagemLlmConversationService>();
        builder.Services.AddSingleton<ITriagemLlmClient, FakeTriagemLlmClient>();
        builder.Services.AddSingleton<ITriagemLlmResponseVerifier, TriagemLlmResponseVerifier>();
        builder.Services.AddScoped<ITriagemLlmUsageRepository, TriagemLlmUsageRepository>();
        builder.Services.AddSingleton<ITriagemLlmConversationContextRepository,
            FakeTriagemLlmConversationContextRepository>();
        builder.Services.AddSingleton(Options.Create(new TriagemLlmConversationOptions()));
        builder.Services.AddSingleton(Options.Create(new TriagemChannelOptions { LeaseSeconds = 60 }));
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
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapTriagemChannelEndpoints();
        await app.StartAsync();

        return (app, capturedDatabaseName);
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

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid TriagemId, Guid WorkOrderId, Guid CommandId);

    private static IngestTriagemChannelMessageRequest CreateInboundMessage(string messageId, string idempotencyKey) =>
        new(
            "1",
            messageId,
            idempotencyKey,
            Guid.NewGuid(),
            "5511999991234",
            "individual",
            false,
            Now,
            "text",
            "O produto não liga.");

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
        return new Seed(actualTenantId, actualIntegrationId, triagemId, workOrderId, command.Id);
    }

    /// <summary>Semeia uma triagem (AguardandoEnvio) e um comando Pending — para os testes de <c>/claim</c>.</summary>
    private static async Task<Seed> SeedPendingCommandAsync(string databaseName,
        FakeTriagemChannelCommandRepository commandRepository)
    {
        await using var context = CreateContext(databaseName);
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var key = new TriagemIdempotencyKey(tenantId, integrationId, "OS-EXT-1");
        var triagem = new Triagem(triagemId, tenantId, integrationId, workOrderId, Now);
        var command = new TriagemChannelCommand(Guid.NewGuid(), tenantId, integrationId, triagemId, workOrderId,
            "template-version-id", 1,
            "conteúdo renderizado", key.ToString(), Guid.NewGuid(), Now, recipientPhone: "+5511999999999");
        context.Add(triagem);
        context.Entry(triagem).Property(TriagemPersistenceSchema.WorkOrderProviderIdShadowPropertyName)
            .CurrentValue = key.WorkOrderProviderId;
        await context.SaveChangesAsync();
        commandRepository.Add(command);

        return new Seed(tenantId, integrationId, triagemId, workOrderId, command.Id);
    }

    /// <summary>
    /// Semeia uma triagem (AguardandoEnvio) e um comando já <c>Claimed</c>
    /// diretamente via <see cref="AtuaDbContext"/> — para os testes de
    /// <c>/complete</c>, evitando depender do claim real (SQL bruto, não
    /// suportado pelo InMemory).
    /// </summary>
    private static async Task<Seed> SeedClaimedCommandAsync(string databaseName)
    {
        await using var context = CreateContext(databaseName);
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var triagemId = Guid.NewGuid();
        var key = new TriagemIdempotencyKey(tenantId, integrationId, "OS-EXT-1");
        var triagem = new Triagem(triagemId, tenantId, integrationId, workOrderId, Now);
        var command = new TriagemChannelCommand(Guid.NewGuid(), tenantId, integrationId, triagemId, workOrderId,
            "template-version-id", 1,
            "conteúdo renderizado", key.ToString(), Guid.NewGuid(), Now);
        command.Claim(Now, Now.AddSeconds(60));
        context.AddRange(triagem, command);
        context.Entry(triagem).Property(TriagemPersistenceSchema.WorkOrderProviderIdShadowPropertyName)
            .CurrentValue = key.WorkOrderProviderId;
        await context.SaveChangesAsync();

        return new Seed(tenantId, integrationId, triagemId, workOrderId, command.Id);
    }

    /// <summary>
    /// Simula uma segunda reivindicação (claim) do mesmo comando — como se a
    /// lease anterior tivesse expirado e outra instância do canal assumisse
    /// o comando com uma nova lease/token (ADR-042, correção de
    /// concorrência). Usado para testar que o titular antigo recebe 409 ao
    /// completar com o token obsoleto, e que apenas o titular vigente
    /// (<paramref name="newClaimedAtUtc"/>) consegue concluir.
    /// </summary>
    private static async Task ReclaimCommandAsync(string databaseName, Guid commandId, DateTimeOffset newClaimedAtUtc)
    {
        await using var context = CreateContext(databaseName);
        var command = await context.TriagemChannelCommands.SingleAsync(c => c.Id == commandId);
        command.Claim(newClaimedAtUtc, newClaimedAtUtc.AddSeconds(60));
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Simula os claims produzidos por <see cref="ServiceCredentialAuthenticationHandler"/>
    /// a partir de headers de teste, sem depender de um banco real de
    /// credenciais. Sempre autentica com sucesso — a ausência de claims
    /// (tenant_id/integration_id) é usada para testar o caminho 401 dentro
    /// do endpoint, e a ausência/divergência do claim <c>scope</c> é usada
    /// para testar o caminho de autorização (403) da policy.
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
