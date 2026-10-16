using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atua.Channel.Api;
using Atua.Channel.Configuration;
using Atua.Channel.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atua.Channel.Tests;

/// <summary>
/// Testes de <see cref="ChannelApiClient"/> contra um <see cref="HttpMessageHandler"/>
/// falso (sem servidor real) — cobrem roteamento, verbo HTTP, cabeçalho de
/// autorização por escopo exclusivo (claim vs. complete) e tratamento de
/// 204/corpo.
/// </summary>
public class ChannelApiClientTests
{
    private const string ClaimToken = "token-claim-exclusivo";
    private const string CompleteToken = "token-complete-exclusivo";

    [Fact]
    public async Task ClaimAsync_QuandoHaComando_Retorna200EDeserializa()
    {
        var expected = new ClaimChannelCommandResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "template-inicial", 1,
            "tenant:integration:OS-1", Guid.NewGuid(), 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(60));

        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expected),
            };
        });

        var client = CreateClient(handler);

        var result = await client.ClaimAsync();

        Assert.NotNull(result);
        Assert.Equal(expected.CommandId, result!.CommandId);
        Assert.Equal(expected.TemplateId, result.TemplateId);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/api/internal/triagens/canal/claim", capturedRequest.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal(ClaimToken, capturedRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ClaimAsync_Quando204_RetornaNull()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler);

        var result = await client.ClaimAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task ClaimAsync_QuandoRespostaDeErro_LancaHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ClaimAsync());
    }

    [Fact]
    public async Task CompleteAsync_EnviaOutcomeEUsaTokenDeComplete()
    {
        var commandId = Guid.NewGuid();
        var expected = new CompleteChannelCommandResponse(commandId, "Completed", DateTimeOffset.UtcNow);

        HttpRequestMessage? capturedRequest = null;
        CompleteChannelCommandRequest? capturedBody = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadFromJsonAsync<CompleteChannelCommandRequest>().GetAwaiter()
                .GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) };
        });

        var client = CreateClient(handler);

        var result = await client.CompleteAsync(commandId, DateTimeOffset.UtcNow, "Success", null, null);

        Assert.Equal(expected.Status, result!.Status);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/api/internal/triagens/canal/complete", capturedRequest.RequestUri!.AbsolutePath);
        Assert.Equal(CompleteToken, capturedRequest.Headers.Authorization!.Parameter);
        Assert.NotNull(capturedBody);
        Assert.Equal(commandId, capturedBody!.CommandId);
        Assert.Equal("Success", capturedBody.Outcome);
    }

    [Fact]
    public async Task CompleteAsync_Quando409_RetornaNullSemLancarExcecao()
    {
        // Lease perdida (ADR-042, correção de concorrência): a Master API
        // respondeu 409 porque o token de lease apresentado não é mais o
        // vigente. O cliente nunca trata isso como erro de transporte.
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Conflict));
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, "Success", null, null);

        Assert.Null(result);
    }

    [Fact]
    public async Task CompleteAsync_EnviaClaimedAtUtcNoCorpo()
    {
        var commandId = Guid.NewGuid();
        var claimedAtUtc = DateTimeOffset.UtcNow;
        var expected = new CompleteChannelCommandResponse(commandId, "Completed", DateTimeOffset.UtcNow);

        CompleteChannelCommandRequest? capturedBody = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            capturedBody = request.Content!.ReadFromJsonAsync<CompleteChannelCommandRequest>().GetAwaiter()
                .GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) };
        });

        var client = CreateClient(handler);

        await client.CompleteAsync(commandId, claimedAtUtc, "Success", null, null);

        Assert.NotNull(capturedBody);
        Assert.Equal(claimedAtUtc, capturedBody!.ClaimedAtUtc);
    }

    private static ChannelApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5240") };
        var options = Options.Create(new ChannelWorkerOptions
        {
            ApiBaseUrl = "http://localhost:5240",
            ClaimServiceToken = ClaimToken,
            CompleteServiceToken = CompleteToken,
            PollingIntervalSeconds = 1,
        });
        return new ChannelApiClient(httpClient, options, NullLogger<ChannelApiClient>.Instance);
    }
}

/// <summary>Handler falso mínimo para testar clientes HTTP tipados sem servidor real.</summary>
internal sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(responder(request, cancellationToken));
}
