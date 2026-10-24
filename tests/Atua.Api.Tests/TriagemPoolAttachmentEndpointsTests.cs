using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.LlmUsage;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

public class TriagemPoolAttachmentEndpointsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TriagemId = Guid.NewGuid();
    private static readonly Guid MediaId = Guid.NewGuid();
    private static string Route(Guid? mediaId = null) =>
        $"/api/tenants/{TenantId}/triage-pool/{TriagemId}/attachments/{mediaId ?? MediaId}/content";

    [Fact]
    public async Task Content_RequiresAuthenticatedBrowserSession()
    {
        await using var app = await CreateAppAsync(new DetailQuery(), new Storage());
        var response = await app.GetTestClient().GetAsync(Route());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(ETriagemPoolDetailOutcome.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(ETriagemPoolDetailOutcome.NotFound, HttpStatusCode.NotFound)]
    public async Task Content_RespectsDetailAuthorization(ETriagemPoolDetailOutcome outcome, HttpStatusCode expected)
    {
        var storage = new Storage();
        await using var app = await CreateAppAsync(new DetailQuery(outcome), storage);
        var response = await Client(app).GetAsync(Route());
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, storage.Reads);
    }

    [Fact]
    public async Task Content_DoesNotReadAnAttachmentOutsideTheAuthorizedTriage()
    {
        var storage = new Storage();
        await using var app = await CreateAppAsync(new DetailQuery(), storage);
        var response = await Client(app).GetAsync(Route(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, storage.Reads);
    }

    [Fact]
    public async Task Content_ReturnsBytesWithoutCachingAndUsesTenantScopedKey()
    {
        var storage = new Storage();
        var query = new DetailQuery();
        await using var app = await CreateAppAsync(query, storage);
        var response = await Client(app).GetAsync(Route());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Storage.Bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(new TriagemInboundMediaStorageKey(TenantId, MediaId), storage.LastKey);
        Assert.Equal((TenantId, TriagemId), query.RequestedIds);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.NotFound)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    public async Task Content_ReportsMissingOrUnavailableStorage(bool unavailable, HttpStatusCode expected)
    {
        var storage = new Storage { Missing = !unavailable, Unavailable = unavailable };
        await using var app = await CreateAppAsync(new DetailQuery(), storage);
        Assert.Equal(expected, (await Client(app).GetAsync(Route())).StatusCode);
    }

    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        return client;
    }

    private static async Task<WebApplication> CreateAppAsync(DetailQuery query, Storage storage)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IGetTriagemPoolDetailUseCase>(query);
        builder.Services.AddSingleton<ITriagemInboundMediaStorage>(storage);
        builder.Services.AddScoped<IListTriagemPoolUseCase>(_ => throw new NotSupportedException());
        builder.Services.AddScoped<IAssumeTriagemUseCase>(_ => throw new NotSupportedException());
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, AuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("BrowserSession",
            policy => policy.AddAuthenticationSchemes("Test").RequireAuthenticatedUser()));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapTriagemPoolEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class AuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var value))
                return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", value.ToString())], "Test"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }

    private sealed class DetailQuery(ETriagemPoolDetailOutcome outcome = ETriagemPoolDetailOutcome.Success)
        : IGetTriagemPoolDetailUseCase
    {
        public (Guid, Guid)? RequestedIds { get; private set; }

        public Task<TriagemPoolDetailResult> ExecuteAsync(Guid tenantId, Guid triagemId, Guid requestingUserId,
            CancellationToken cancellationToken)
        {
            RequestedIds = (tenantId, triagemId);
            if (outcome != ETriagemPoolDetailOutcome.Success)
                return Task.FromResult(TriagemPoolDetailResult.Failure(outcome));
            var now = DateTimeOffset.UtcNow;
            var detail = new TriagemPoolDetailView(TriagemId, ETriagemStatus.EmColeta, "", now, null, null,
                Guid.NewGuid(), now, null, new WarrantyEstimate(EWarrantyEstimateStatus.Indeterminado, null, 12),
                null, null, new TriagemLlmCostSummary(0, 0, 0, 0),
                new TriagemPoolWorkOrderView(Guid.NewGuid(), null, null, null, null, null, null, null,
                    null, null, null, null, null), [],
                [new TriagemPoolAttachmentView(MediaId, ETriagemInboundMediaKind.Document, "application/pdf",
                    false, null, null, now, new Uri("https://example.com/document.pdf"), now.AddMinutes(15))], []);
            return Task.FromResult(new TriagemPoolDetailResult(outcome, detail));
        }
    }

    private sealed class Storage : ITriagemInboundMediaStorage
    {
        public static readonly byte[] Bytes = "%PDF-1.7"u8.ToArray();
        public bool Missing { get; init; }
        public bool Unavailable { get; init; }
        public int Reads { get; private set; }
        public TriagemInboundMediaStorageKey? LastKey { get; private set; }

        public Task<TriagemInboundMediaContent?> GetAsync(TriagemInboundMediaStorageKey key, CancellationToken ct)
        {
            Reads++;
            LastKey = key;
            if (Unavailable) throw new TriagemInboundMediaStorageUnavailableException("test");
            return Task.FromResult<TriagemInboundMediaContent?>(Missing ? null : new(Bytes, "application/pdf", Bytes.Length));
        }

        public Task<ETriagemInboundMediaPutOutcome> PutIfAbsentAsync(TriagemInboundMediaStorageKey key,
            Stream content, string contentType, long contentLength, CancellationToken ct) => throw new NotSupportedException();
        public Task<ETriagemInboundMediaDeleteOutcome> DeleteAsync(TriagemInboundMediaStorageKey key,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<Uri> CreatePresignedReadUrlAsync(TriagemInboundMediaStorageKey key, TimeSpan expiresIn,
            CancellationToken ct) => throw new NotSupportedException();
    }
}
