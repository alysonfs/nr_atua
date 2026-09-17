using Atua.Api.Application.Integrations.CollectorControl;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de RF-025/ADR-029 — agendador recorrente de coleta. Cobre apenas a
/// varredura e criação de <see cref="ImmediateCollectionCommand"/>s; nada
/// aqui executa o comando (Worker, ADR-021).
/// </summary>
public class RecurrentCollectionSchedulerServiceTests
{
    [Fact]
    public async Task IntegracaoInactiveNuncaGeraComando()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context, activationStatus: ECollectorActivationStatus.Inactive,
            lastRequestedAtUtc: DateTimeOffset.UtcNow.AddHours(-1));
        var service = CreateService(context, DateTimeOffset.UtcNow);

        var created = await service.ScheduleDueCollectionsAsync(CancellationToken.None);

        Assert.Equal(0, created);
        Assert.Empty(context.ImmediateCollectionCommands
            .Where(command => command.Status == EImmediateCollectionCommandStatus.Pending));
    }

    [Fact]
    public async Task IntegracaoAtivaSemComandoAnteriorNaoGeraComandoNestaVarredura()
    {
        // ADR-029: ActivateAsync já cria o primeiro comando sincronamente; o
        // scheduler assume apenas a partir do segundo ciclo.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, activationStatus: ECollectorActivationStatus.Active,
            lastRequestedAtUtc: null);
        var service = CreateService(context, DateTimeOffset.UtcNow);

        var created = await service.ScheduleDueCollectionsAsync(CancellationToken.None);

        Assert.Equal(0, created);
        Assert.Empty(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task IntegracaoAtivaComIntervaloVencidoGeraNovoComandoPendente()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        var seed = await SeedAsync(context, activationStatus: ECollectorActivationStatus.Active,
            lastRequestedAtUtc: now.AddMinutes(-20), intervalMinutes: 15,
            lastCommandStatus: EImmediateCollectionCommandStatus.Succeeded);
        var service = CreateService(context, now);

        var created = await service.ScheduleDueCollectionsAsync(CancellationToken.None);

        Assert.Equal(1, created);
        var pending = await context.ImmediateCollectionCommands.SingleAsync(
            command => command.Status == EImmediateCollectionCommandStatus.Pending);
        Assert.Equal(seed.IntegrationId, pending.IntegrationId);
    }

    [Fact]
    public async Task IntegracaoAtivaComIntervaloAindaNaoVencidoNaoGeraComando()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        await SeedAsync(context, activationStatus: ECollectorActivationStatus.Active,
            lastRequestedAtUtc: now.AddMinutes(-10), intervalMinutes: 15,
            lastCommandStatus: EImmediateCollectionCommandStatus.Succeeded);
        var service = CreateService(context, now);

        var created = await service.ScheduleDueCollectionsAsync(CancellationToken.None);

        Assert.Equal(0, created);
    }

    [Fact]
    public async Task AlteracaoDeIntervaloEhRespeitadaNaProximaVarreduraSemRecriarComandoEmAndamento()
    {
        // RF-025.7/RN-025.7: alterar o intervalo não cancela nem recria um
        // comando Pending/Claimed em andamento.
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        var seed = await SeedAsync(context, activationStatus: ECollectorActivationStatus.Active,
            lastRequestedAtUtc: now.AddMinutes(-20), intervalMinutes: 15,
            lastCommandStatus: EImmediateCollectionCommandStatus.Pending);

        var integration = await context.Integrations.SingleAsync(item => item.Id == seed.IntegrationId);
        integration.SetRecurrentCollectionIntervalMinutes(60, now);
        await context.SaveChangesAsync();

        var service = CreateService(context, now);
        var created = await service.ScheduleDueCollectionsAsync(CancellationToken.None);

        Assert.Equal(0, created);
        var command = await context.ImmediateCollectionCommands.SingleAsync();
        Assert.Equal(EImmediateCollectionCommandStatus.Pending, command.Status);
    }

    [Fact]
    public async Task NaoDuplicaComandoPendenteSobConcorrenciaSimulada()
    {
        // Simula duas varreduras consecutivas antes da conclusão do primeiro
        // comando criado — o segundo tick deve ver o Pending em aberto.
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        await SeedAsync(context, activationStatus: ECollectorActivationStatus.Active,
            lastRequestedAtUtc: now.AddMinutes(-20), intervalMinutes: 15,
            lastCommandStatus: EImmediateCollectionCommandStatus.Succeeded);
        var service = CreateService(context, now);

        var firstRun = await service.ScheduleDueCollectionsAsync(CancellationToken.None);
        var secondRun = await service.ScheduleDueCollectionsAsync(CancellationToken.None);

        Assert.Equal(1, firstRun);
        Assert.Equal(0, secondRun);
        Assert.Equal(1, await context.ImmediateCollectionCommands.CountAsync(
            command => command.Status == EImmediateCollectionCommandStatus.Pending));
    }

    [Fact]
    public async Task IntervaloPadraoDe15MinutosEhUsadoQuandoNaoAlterado()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        await SeedAsync(context, activationStatus: ECollectorActivationStatus.Active,
            lastRequestedAtUtc: now.AddMinutes(-16), intervalMinutes: null,
            lastCommandStatus: EImmediateCollectionCommandStatus.Succeeded);
        var service = CreateService(context, now);

        var created = await service.ScheduleDueCollectionsAsync(CancellationToken.None);

        Assert.Equal(1, created);
    }

    private static RecurrentCollectionSchedulerService CreateService(AtuaDbContext context,
        DateTimeOffset now) => new(context, new FakeTimeProvider(now));

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record Seed(Guid TenantId, Guid IntegrationId);

    private static async Task<Seed> SeedAsync(AtuaDbContext context,
        ECollectorActivationStatus activationStatus, DateTimeOffset? lastRequestedAtUtc,
        int? intervalMinutes = null,
        EImmediateCollectionCommandStatus lastCommandStatus = EImmediateCollectionCommandStatus.Succeeded)
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Fabricante",
            new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        if (intervalMinutes is not null)
        {
            integration.SetRecurrentCollectionIntervalMinutes(intervalMinutes.Value, DateTimeOffset.UtcNow);
        }

        context.AddRange(tenant, provider, integration);

        var activation = new CollectorActivation(Guid.CreateVersion7(), tenant.Id, integration.Id);
        if (activationStatus == ECollectorActivationStatus.Active)
        {
            activation.Activate(DateTimeOffset.UtcNow);
        }
        context.CollectorActivations.Add(activation);

        if (lastRequestedAtUtc is not null)
        {
            var command = new ImmediateCollectionCommand(Guid.CreateVersion7(), tenant.Id, integration.Id,
                provider.Id, lastRequestedAtUtc.Value);
            if (lastCommandStatus == EImmediateCollectionCommandStatus.Claimed)
            {
                command.TryClaim(lastRequestedAtUtc.Value, TimeSpan.FromMinutes(30));
            }
            else if (lastCommandStatus == EImmediateCollectionCommandStatus.Succeeded)
            {
                command.TryClaim(lastRequestedAtUtc.Value, TimeSpan.FromMinutes(30));
                command.TryComplete(EImmediateCollectionCommandStatus.Succeeded,
                    lastRequestedAtUtc.Value.AddMinutes(1));
            }

            context.ImmediateCollectionCommands.Add(command);
        }

        await context.SaveChangesAsync();
        return new Seed(tenant.Id, integration.Id);
    }
}
