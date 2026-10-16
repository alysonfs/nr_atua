using Atua.Api.Application.Triagens;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

public sealed class TriagemMessageTemplateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OwnerSalvaRascunhoEPublicaNovaVersaoImutavel()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);
        const string content =
            "Nova mensagem da {{nome_posto}} / {{fabricante}} sobre a OS {{numero_os}} e {{produto}}.";

        var draftResult = await service.SaveDraftAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            ETriagemMessageTemplateType.InitialContact, content, CancellationToken.None);
        var publishResult = await service.PublishAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            ETriagemMessageTemplateType.InitialContact, CancellationToken.None);

        Assert.Equal(ETriagemTemplateOperationStatus.Success, draftResult.Status);
        Assert.Equal(2, draftResult.Templates.Single().Version);
        Assert.Equal(ETriagemTemplateOperationStatus.Success, publishResult.Status);
        Assert.True(publishResult.Templates.Single().IsActive);
        Assert.Equal(ETriagemMessageTemplateStatus.Published, publishResult.Templates.Single().Status);

        var versions = await context.TriagemMessageTemplates
            .Where(item => item.Type == ETriagemMessageTemplateType.InitialContact)
            .OrderBy(item => item.Version)
            .ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.False(versions[0].IsActive);
        Assert.Equal(TriagemMessageTemplateDefaults.InitialContact, versions[0].Content);
        Assert.True(versions[1].IsActive);
        Assert.Equal(content, versions[1].Content);
    }

    [Fact]
    public async Task PlaceholderDesconhecidoBloqueiaPublicacao()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);
        await service.SaveDraftAsync(seed.AdminId, seed.TenantId, seed.IntegrationId,
            ETriagemMessageTemplateType.InitialContact, "Olá, {{cliente}}.", CancellationToken.None);

        var result = await service.PublishAsync(seed.AdminId, seed.TenantId, seed.IntegrationId,
            ETriagemMessageTemplateType.InitialContact, CancellationToken.None);

        Assert.Equal(ETriagemTemplateOperationStatus.ValidationFailed, result.Status);
        Assert.Contains("unknown_placeholder:cliente", result.ValidationErrors);
        var draft = await context.TriagemMessageTemplates.SingleAsync(item =>
            item.Type == ETriagemMessageTemplateType.InitialContact
            && item.Status == ETriagemMessageTemplateStatus.Draft);
        Assert.False(draft.IsActive);
    }

    [Fact]
    public async Task ItensPendentesEhRecusadoNoContatoInicialEAceitoNoLembrete()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var initial = await service.PreviewAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            ETriagemMessageTemplateType.InitialContact, "{{itens_pendentes}}", CancellationToken.None);
        var reminder = await service.PreviewAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            ETriagemMessageTemplateType.Reminder24Hours,
            "OS {{numero_os}}: {{itens_pendentes}}",
            CancellationToken.None);

        Assert.Equal(ETriagemTemplateOperationStatus.ValidationFailed, initial.Status);
        Assert.Contains("unknown_placeholder:itens_pendentes", initial.ValidationErrors);
        Assert.Equal(ETriagemTemplateOperationStatus.Success, reminder.Status);
        Assert.Contains("foto legível da nota fiscal", reminder.Preview);
    }

    [Theory]
    [InlineData("Olá, {{NOME_POSTO}}.", "unknown_placeholder:NOME_POSTO")]
    [InlineData("Olá, {{nome_posto.", "malformed_placeholder")]
    public async Task PlaceholderInvalidoOuMalformadoBloqueiaPreview(string content, string expectedError)
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.PreviewAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            ETriagemMessageTemplateType.InitialContact, content, CancellationToken.None);

        Assert.Equal(ETriagemTemplateOperationStatus.ValidationFailed, result.Status);
        Assert.Contains(expectedError, result.ValidationErrors);
    }

    [Fact]
    public async Task MembroDefaultPodeLerMasNaoPodeAlterarTemplates()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var list = await service.ListAsync(seed.ViewerId, seed.TenantId, seed.IntegrationId,
            CancellationToken.None);
        var save = await service.SaveDraftAsync(seed.ViewerId, seed.TenantId, seed.IntegrationId,
            ETriagemMessageTemplateType.InitialContact, "Mensagem", CancellationToken.None);

        Assert.Equal(ETriagemTemplateOperationStatus.Success, list.Status);
        Assert.Equal(3, list.Templates.Count);
        Assert.Equal(ETriagemTemplateOperationStatus.Forbidden, save.Status);
    }

    [Fact]
    public async Task ResolverUsaVersaoAtivaEFallbackDeProduto()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var resolver = new TriagemMessageTemplateRenderer(context);

        var result = await resolver.ResolveInitialContactAsync(
            seed.TenantId,
            seed.IntegrationId,
            "provider-id",
            "OS-123",
            null,
            null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
        Assert.Contains("Natal Refrigeração", result.RenderedContent);
        Assert.Contains("Midea", result.RenderedContent);
        Assert.Contains("OS-123", result.RenderedContent);
        Assert.Contains("seu produto", result.RenderedContent);
        Assert.DoesNotContain("{{", result.RenderedContent);
    }

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TriagemMessageTemplateService CreateService(AtuaDbContext context) =>
        new(context, new FakeTimeProvider(Now));

    private sealed record Seed(
        Guid TenantId,
        Guid IntegrationId,
        Guid OwnerId,
        Guid AdminId,
        Guid ViewerId);

    private static async Task<Seed> SeedAsync(AtuaDbContext context)
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Natal Refrigeração", "11122233000183",
            "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Midea",
            new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var owner = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        var admin = new User(Guid.CreateVersion7(), null, "admin@atua.com", "hash");
        var viewer = new User(Guid.CreateVersion7(), null, "viewer@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner, admin, viewer);
        context.TenantMemberships.AddRange(
            new TenantMembership(tenant.Id, owner.Id, ETenantMembershipRole.Owner),
            new TenantMembership(tenant.Id, admin.Id, ETenantMembershipRole.Admin),
            new TenantMembership(tenant.Id, viewer.Id, ETenantMembershipRole.Default));
        context.TriagemMessageTemplates.AddRange(
            TriagemMessageTemplateDefaults.CreatePublishedTemplates(tenant.Id, integration.Id, Now));
        await context.SaveChangesAsync();
        return new Seed(tenant.Id, integration.Id, owner.Id, admin.Id, viewer.Id);
    }
}
