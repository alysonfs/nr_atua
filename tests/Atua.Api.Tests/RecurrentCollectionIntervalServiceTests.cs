using Atua.Api.Application.Integrations;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de RF-025 (Coleta Recorrente Configurável) — leitura/alteração do
/// intervalo pelo Office. Escopo: validação de limites, autorização e
/// persistência do valor. O agendador propriamente dito é coberto por
/// <see cref="RecurrentCollectionSchedulerServiceTests"/>.
/// </summary>
public class RecurrentCollectionIntervalServiceTests
{
    [Fact]
    public async Task ValorPadraoEh15MinutosQuandoNaoPersistido()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = new RecurrentCollectionIntervalService(context, TimeProvider.System);

        var result = await service.GetAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            CancellationToken.None);

        Assert.Equal(ERecurrentCollectionIntervalStatus.Success, result.Status);
        Assert.Equal(15, result.View!.RecurrentCollectionIntervalMinutes);
    }

    [Fact]
    public async Task OwnerAlteraIntervaloEValorEhPersistido()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = new RecurrentCollectionIntervalService(context, TimeProvider.System);

        var result = await service.SetAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, 60,
            CancellationToken.None);

        Assert.Equal(ERecurrentCollectionIntervalStatus.Success, result.Status);
        Assert.Equal(60, result.View!.RecurrentCollectionIntervalMinutes);
        var integration = await context.Integrations.SingleAsync(item => item.Id == seed.IntegrationId);
        Assert.Equal(60, integration.RecurrentCollectionIntervalMinutes);
    }

    [Fact]
    public async Task AdminTambemPodeAlterarIntervalo()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = new RecurrentCollectionIntervalService(context, TimeProvider.System);

        var result = await service.SetAsync(seed.AdminId, seed.TenantId, seed.IntegrationId, 30,
            CancellationToken.None);

        Assert.Equal(ERecurrentCollectionIntervalStatus.Success, result.Status);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public async Task IntervaloForaDosLimitesEhRecusado(int minutes)
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = new RecurrentCollectionIntervalService(context, TimeProvider.System);

        var result = await service.SetAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, minutes,
            CancellationToken.None);

        Assert.Equal(ERecurrentCollectionIntervalStatus.InvalidInterval, result.Status);
        var integration = await context.Integrations.SingleAsync(item => item.Id == seed.IntegrationId);
        Assert.Equal(15, integration.RecurrentCollectionIntervalMinutes);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1440)]
    public async Task IntervaloNosLimitesEhAceito(int minutes)
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = new RecurrentCollectionIntervalService(context, TimeProvider.System);

        var result = await service.SetAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, minutes,
            CancellationToken.None);

        Assert.Equal(ERecurrentCollectionIntervalStatus.Success, result.Status);
    }

    [Fact]
    public async Task UsuarioSemMembershipNaoConsegueLerNemAlterar()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var outsider = new User(Guid.CreateVersion7(), null, "outsider@atua.com", "hash");
        context.Users.Add(outsider);
        await context.SaveChangesAsync();
        var service = new RecurrentCollectionIntervalService(context, TimeProvider.System);

        var getResult = await service.GetAsync(outsider.Id, seed.TenantId, seed.IntegrationId,
            CancellationToken.None);
        var setResult = await service.SetAsync(outsider.Id, seed.TenantId, seed.IntegrationId, 30,
            CancellationToken.None);

        Assert.Equal(ERecurrentCollectionIntervalStatus.Forbidden, getResult.Status);
        Assert.Equal(ERecurrentCollectionIntervalStatus.Forbidden, setResult.Status);
    }

    [Fact]
    public async Task MembroComPapelDiferenteDeOwnerOuAdminNaoConsegueAlterar()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var viewer = new User(Guid.CreateVersion7(), null, "viewer@atua.com", "hash");
        context.Users.Add(viewer);
        context.TenantMemberships.Add(new TenantMembership(seed.TenantId, viewer.Id,
            ETenantMembershipRole.Default));
        await context.SaveChangesAsync();
        var service = new RecurrentCollectionIntervalService(context, TimeProvider.System);

        var result = await service.SetAsync(viewer.Id, seed.TenantId, seed.IntegrationId, 30,
            CancellationToken.None);

        Assert.Equal(ERecurrentCollectionIntervalStatus.Forbidden, result.Status);
    }

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid OwnerId, Guid AdminId);

    private static async Task<Seed> SeedAsync(AtuaDbContext context)
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Fabricante",
            new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var owner = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        var admin = new User(Guid.CreateVersion7(), null, "admin@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner, admin);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, owner.Id, ETenantMembershipRole.Owner));
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, admin.Id, ETenantMembershipRole.Admin));
        await context.SaveChangesAsync();
        return new Seed(tenant.Id, integration.Id, owner.Id, admin.Id);
    }
}
