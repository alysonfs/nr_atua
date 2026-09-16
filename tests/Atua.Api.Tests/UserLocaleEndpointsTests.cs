using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.Identity;
using Atua.Api.Domain.Identity;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atua.Api.Tests;

public class UserLocaleEndpointsTests
{
    [Fact]
    public async Task GetRetornaLocalePersistidoDoUsuarioAutenticado()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        await SeedUserAsync(databaseName, userId, UserLocale.SpanishArgentina);
        var client = CreateAuthenticatedClient(app, userId);

        var response = await client.GetAsync("/api/users/me/locale");
        var body = await response.Content.ReadFromJsonAsync<UserLocaleResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(UserLocale.SpanishArgentina, body!.Locale);
    }

    [Theory]
    [InlineData(UserLocale.Default)]
    [InlineData(UserLocale.EnglishUnitedStates)]
    [InlineData(UserLocale.SpanishArgentina)]
    public async Task PutPersisteLocaleSuportado(string locale)
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        await SeedUserAsync(databaseName, userId);
        var client = CreateAuthenticatedClient(app, userId);

        var response = await client.PutAsJsonAsync("/api/users/me/locale",
            new SetUserLocaleRequest(locale));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var context = CreateContext(databaseName);
        Assert.Equal(locale, (await context.Users.FindAsync(userId))!.PreferredLocale);
    }

    [Fact]
    public async Task PutRetornaInvalidLocaleSemAlterarPreferencia()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        await SeedUserAsync(databaseName, userId);
        var client = CreateAuthenticatedClient(app, userId);

        var response = await client.PutAsJsonAsync("/api/users/me/locale",
            new SetUserLocaleRequest("fr-FR"));
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_locale", body!.Error);
        await using var context = CreateContext(databaseName);
        Assert.Equal(UserLocale.Default,
            (await context.Users.FindAsync(userId))!.PreferredLocale);
    }

    [Fact]
    public async Task GetEPutExigemBrowserSession()
    {
        var (app, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var client = app.GetTestClient();

        var getResponse = await client.GetAsync("/api/users/me/locale");
        var putResponse = await client.PutAsJsonAsync("/api/users/me/locale",
            new SetUserLocaleRequest(UserLocale.Default));

        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, putResponse.StatusCode);
    }

    private static async Task SeedUserAsync(string databaseName, Guid userId,
        string? locale = null)
    {
        await using var context = CreateContext(databaseName);
        var user = new User(userId, null, "owner@atua.com", "hash");
        if (locale is not null) user.SetPreferredLocale(locale);
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static async Task<(WebApplication App, string DatabaseName)> CreateApplicationAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AtuaDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<UserLocalePreferenceService>();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("BrowserSession", policy =>
                policy.AddAuthenticationSchemes("Test").RequireAuthenticatedUser());
        });
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapUserLocaleEndpoints();
        await app.StartAsync();
        return (app, databaseName);
    }

    private static HttpClient CreateAuthenticatedClient(WebApplication app, Guid userId)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-User-Id", userId.ToString());
        return client;
    }

    private sealed class TestAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Test-User-Id", out var userId))
            {
                return Task.FromResult(AuthenticateResult.Fail("missing user"));
            }

            var claims = new[] { new Claim("sub", userId.ToString()) };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed record ErrorResponse(string Error);
}
