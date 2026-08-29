using System.Net;
using System.Net.Http.Json;
using Atua.Api.Application.Identity;
using Atua.Api.Application.Billing;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

public class AuthEndpointsTests
{
    [Fact]
    public async Task SignUpRetornaAcceptedSemExporCodigoDeConfirmacao()
    {
        await using var app = CreateApplication();
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/auth/signup", new SignUpRequest(
            "owner@atua.com", "senha123", "senha123"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain("482913", body);
    }

    [Fact]
    public async Task ConfirmEmailRetornaOkQuandoCodigoValido()
    {
        await using var app = CreateApplication();
        await app.StartAsync();
        var client = app.GetTestClient();

        await client.PostAsJsonAsync("/auth/signup", new SignUpRequest(
            "owner@atua.com", "senha123", "senha123"));

        var response = await client.PostAsJsonAsync("/auth/confirm-email", new ConfirmEmailRequest(
            "owner@atua.com", "482913"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmEmailRetornaBadRequestQuandoCodigoInvalido()
    {
        await using var app = CreateApplication();
        await app.StartAsync();
        var client = app.GetTestClient();

        await client.PostAsJsonAsync("/auth/signup", new SignUpRequest(
            "owner@atua.com", "senha123", "senha123"));

        var response = await client.PostAsJsonAsync("/auth/confirm-email", new ConfirmEmailRequest(
            "owner@atua.com", "000000"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static WebApplication CreateApplication()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AtuaDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<ISecretHasher, FakeSecretHasher>();
        builder.Services.AddSingleton<ITokenHashService, TokenHashService>();
        builder.Services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        builder.Services.Configure<AuthOptions>(options =>
            options.SigningKey = "01234567890123456789012345678901");
        builder.Services.AddSingleton<IEmailConfirmationCodeGenerator,
            FixedEmailConfirmationCodeGenerator>();
        builder.Services.AddSingleton<IEmailConfirmationSender,
            FakeEmailConfirmationSender>();
        builder.Services.AddScoped<SignUpService>();
        builder.Services.AddScoped<ConfirmEmailService>();
        builder.Services.AddScoped<CreateTrialService>();
        builder.Services.AddScoped<AuthService>();

        var app = builder.Build();
        app.MapAuthEndpoints();

        return app;
    }

    private sealed class FakeSecretHasher : ISecretHasher
    {
        public string Hash(string value) => $"hash:{value}";

        public bool Verify(string value, string hash) => hash == Hash(value);
    }

    private sealed class FixedEmailConfirmationCodeGenerator : IEmailConfirmationCodeGenerator
    {
        public string Generate() => "482913";
    }

    private sealed class FakeEmailConfirmationSender : IEmailConfirmationSender
    {
        public Task SendAsync(string email, string code, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}