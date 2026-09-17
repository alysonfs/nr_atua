using Atua.Api.Application.Integrations.CollectorControl;
using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de RF-008/ADR-020 — controle de estado de ativação e criação do
/// comando imediato em "Pendente". Nada executa o comando neste portão.
/// </summary>
public class CollectorActivationServiceTests
{
    [Fact]
    public async Task OwnerAtivaAgenteECriaExatamenteUmComandoPendente()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);

        var result = await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.Success, result.Status);
        Assert.Equal("Active", result.View!.Status);
        Assert.NotNull(result.View.ActivatedAtUtc);

        var command = Assert.Single(context.ImmediateCollectionCommands);
        Assert.Equal(EImmediateCollectionCommandStatus.Pending, command.Status);
        Assert.Equal(seed.TenantId, command.TenantId);
        Assert.Equal(seed.IntegrationId, command.IntegrationId);
        Assert.Equal(result.View.LastImmediateCommand!.CommandId, command.Id);
    }

    [Fact]
    public async Task AdminTambemPodeAtivar()
    {
        // RF-008.2: OWNER e ADMIN podem ativar/desativar.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);

        var result = await service.ActivateAsync(seed.AdminId, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.Success, result.Status);
        Assert.Single(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task AtivacaoEhRecusadaQuandoPlanoInelegivelENenhumComandoEhCriado()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: false,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);

        var result = await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.NotEligible, result.Status);
        Assert.Equal(EActivationBlockReason.PlanIneligible, result.BlockReason);
        Assert.Empty(context.ImmediateCollectionCommands);
        Assert.Empty(context.CollectorActivations);
    }

    [Fact]
    public async Task AtivacaoEhPermitidaMesmoComCredencialNaoSucceeded()
    {
        // ADR-024: a validação de credencial não bloqueia mais a ativação.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Failed);
        var service = CreateService(context);

        var result = await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.Success, result.Status);
        Assert.Single(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task AtivacaoEhPermitidaMesmoComCredencialAusente()
    {
        // ADR-024: a validação de credencial não bloqueia mais a ativação.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true, credentialStatus: null);
        var service = CreateService(context);

        var result = await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.Success, result.Status);
        Assert.Single(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task DuplaAtivacaoComMesmaChaveNaoCriaDoisComandos()
    {
        // RN-008.4 + idempotência do ADR-020.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);

        var first = await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);
        var second = await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.Success, second.Status);
        Assert.Single(context.ImmediateCollectionCommands);
        Assert.Equal(first.View!.LastImmediateCommand!.CommandId,
            second.View!.LastImmediateCommand!.CommandId);
        Assert.Single(context.CollectorControlIdempotencies);
    }

    [Fact]
    public async Task DuplaAtivacaoComChavesDiferentesNaoCriaDoisComandosPendentes()
    {
        // O invariante "no máximo um Pending por integração" não depende da
        // chave de idempotência.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);

        await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, "chave-1",
            CancellationToken.None);
        await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, "chave-2",
            CancellationToken.None);

        Assert.Single(context.ImmediateCollectionCommands);
        Assert.Equal(1, await context.ImmediateCollectionCommands.CountAsync(
            command => command.Status == EImmediateCollectionCommandStatus.Pending));
    }

    [Fact]
    public async Task ReusarChaveComConteudoDiferenteDevolveConflitoSemMutar()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);
        await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, "chave-1",
            CancellationToken.None);

        var result = await service.ActivateAsync(seed.AdminId, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.IdempotencyKeyConflict, result.Status);
        Assert.Single(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task AtivacaoSemChaveDeIdempotenciaEhRecusada()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);

        var result = await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            null, CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.MissingIdempotencyKey, result.Status);
        Assert.Empty(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task DesativacaoManualCancelaComandoPendente()
    {
        // RF-008.5/RN-008.5.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);
        await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, "chave-1",
            CancellationToken.None);

        var result = await service.DeactivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            "chave-2", CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.Success, result.Status);
        Assert.Equal("Inactive", result.View!.Status);

        var command = Assert.Single(context.ImmediateCollectionCommands);
        Assert.Equal(EImmediateCollectionCommandStatus.Cancelled, command.Status);
        Assert.Equal(ECollectorDeactivationReason.Manual, command.CancellationReason);

        var activation = Assert.Single(context.CollectorActivations);
        Assert.Equal(ECollectorActivationStatus.Inactive, activation.Status);
        Assert.Equal(ECollectorDeactivationReason.Manual, activation.DeactivationReason);
    }

    [Fact]
    public async Task ComandoCanceladoNuncaVoltaAPendenteENovaAtivacaoCriaComandoDistinto()
    {
        // RF-008.7/RN-008.4.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);
        var first = await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);
        await service.DeactivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, "chave-2",
            CancellationToken.None);

        var second = await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            "chave-3", CancellationToken.None);

        var firstId = first.View!.LastImmediateCommand!.CommandId;
        var secondId = second.View!.LastImmediateCommand!.CommandId;
        Assert.NotEqual(firstId, secondId);

        var cancelled = await context.ImmediateCollectionCommands.SingleAsync(c => c.Id == firstId);
        Assert.Equal(EImmediateCollectionCommandStatus.Cancelled, cancelled.Status);
        var created = await context.ImmediateCollectionCommands.SingleAsync(c => c.Id == secondId);
        Assert.Equal(EImmediateCollectionCommandStatus.Pending, created.Status);
    }

    [Fact]
    public async Task ReconciliacaoDesativaECancelaQuandoPlanoExpira()
    {
        // RF-008.6/RF-020.7.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);
        await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, "chave-1",
            CancellationToken.None);

        context.TenantPlans.RemoveRange(context.TenantPlans);
        await context.SaveChangesAsync();

        await service.ReconcileEligibilityAsync(seed.TenantId, seed.IntegrationId, CancellationToken.None);
        await context.SaveChangesAsync();

        var activation = Assert.Single(context.CollectorActivations);
        Assert.Equal(ECollectorActivationStatus.Inactive, activation.Status);
        Assert.Equal(ECollectorDeactivationReason.PlanIneligible, activation.DeactivationReason);
        var command = Assert.Single(context.ImmediateCollectionCommands);
        Assert.Equal(EImmediateCollectionCommandStatus.Cancelled, command.Status);
    }

    [Fact]
    public async Task ReconciliacaoNaoReativaAgenteQuandoCondicoesVoltamASerValidas()
    {
        // RF-008.7: recuperar a elegibilidade não reativa nada sozinho.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);
        await service.ActivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, "chave-1",
            CancellationToken.None);
        await service.DeactivateAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId, "chave-2",
            CancellationToken.None);

        await service.ReconcileEligibilityAsync(seed.TenantId, seed.IntegrationId, CancellationToken.None);
        await context.SaveChangesAsync();

        var activation = Assert.Single(context.CollectorActivations);
        Assert.Equal(ECollectorActivationStatus.Inactive, activation.Status);
        Assert.Equal(EImmediateCollectionCommandStatus.Cancelled,
            (await context.ImmediateCollectionCommands.SingleAsync()).Status);
    }

    [Fact]
    public async Task UsuarioSemMembershipNaoConsegueAtivarNemLer()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var outsider = new User(Guid.CreateVersion7(), null, "outsider@atua.com", "hash");
        context.Users.Add(outsider);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var activation = await service.ActivateAsync(outsider.Id, seed.TenantId, seed.IntegrationId,
            "chave-1", CancellationToken.None);
        var view = await service.GetAsync(outsider.Id, seed.TenantId, seed.IntegrationId,
            CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.Forbidden, activation.Status);
        Assert.Null(view);
        Assert.Empty(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task MembroDeOutroTenantNaoAlteraNemVeEstadoDoTenantAlheio()
    {
        // RN-008.1: isolamento multi-tenant rigoroso.
        await using var context = CreateContext();
        var victim = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var attacker = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded, cnpj: "22233344000199",
            emailPrefix: "outro");
        var service = CreateService(context);

        var activation = await service.ActivateAsync(attacker.OwnerId, victim.TenantId,
            victim.IntegrationId, "chave-1", CancellationToken.None);
        var view = await service.GetAsync(attacker.OwnerId, victim.TenantId, victim.IntegrationId,
            CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.Forbidden, activation.Status);
        Assert.Null(view);
        Assert.Empty(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task AtivarIntegracaoDeOutroTenantComMembershipProprioNaoEncontraRecurso()
    {
        // Mesmo com membership válido no próprio tenant, um integrationId de
        // outro tenant não pode ser alcançado (RN-008.1).
        await using var context = CreateContext();
        var victim = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var attacker = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded, cnpj: "22233344000199",
            emailPrefix: "outro");
        var service = CreateService(context);

        var result = await service.ActivateAsync(attacker.OwnerId, attacker.TenantId,
            victim.IntegrationId, "chave-1", CancellationToken.None);

        Assert.Equal(ECollectorActivationStatusResult.IntegrationNotFound, result.Status);
        Assert.Empty(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task EstadoInicialEhDesativadoESemComando()
    {
        // RF-008.1.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var service = CreateService(context);

        var view = await service.GetAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal("Inactive", view!.Status);
        Assert.True(view.CanActivate);
        Assert.Equal("None", view.ActivationBlockReason);
        Assert.Equal("Succeeded", view.CredentialValidationStatus);
        Assert.Null(view.LastImmediateCommand);
        Assert.Empty(context.CollectorActivations);
    }

    [Fact]
    public async Task LeituraExpoeDataDaUltimaColetaComSucesso()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);

        var succeededAt = DateTimeOffset.UtcNow.AddHours(-2);
        var succeeded = new ImmediateCollectionCommand(Guid.NewGuid(), seed.TenantId, seed.IntegrationId,
            Guid.NewGuid(), succeededAt.AddMinutes(-5));
        succeeded.TryClaim(succeededAt.AddMinutes(-1), TimeSpan.FromMinutes(30));
        succeeded.TryComplete(EImmediateCollectionCommandStatus.Succeeded, succeededAt);
        context.ImmediateCollectionCommands.Add(succeeded);

        // Comando mais recente, porém sem sucesso: não deve substituir a data acima.
        var failedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var failed = new ImmediateCollectionCommand(Guid.NewGuid(), seed.TenantId, seed.IntegrationId,
            Guid.NewGuid(), failedAt.AddMinutes(-2));
        failed.TryClaim(failedAt.AddMinutes(-1), TimeSpan.FromMinutes(30));
        failed.TryFail(ECommandFailureReason.IServiceUnavailable, failedAt);
        context.ImmediateCollectionCommands.Add(failed);

        await context.SaveChangesAsync();
        var service = CreateService(context);

        var view = await service.GetAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(succeededAt, view!.LastSuccessfulCollectionAtUtc);
    }

    [Fact]
    public async Task LeituraInformaMotivoDeIndisponibilidadeSemSegredo()
    {
        // RF-008.8/RN-008.6.
        await using var context = CreateContext();
        var seed = await SeedAsync(context, trialActive: false, credentialStatus: null);
        var service = CreateService(context);

        var view = await service.GetAsync(seed.OwnerId, seed.TenantId, seed.IntegrationId,
            CancellationToken.None);

        Assert.NotNull(view);
        Assert.False(view!.CanActivate);
        Assert.Equal("PlanIneligible", view.ActivationBlockReason);
        var serialized = System.Text.Json.JsonSerializer.Serialize(view);
        Assert.DoesNotContain("senha", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ciphertext", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private static CollectorActivationService CreateService(AtuaDbContext context) =>
        new(context,
            new CollectorEligibilityEvaluator(context, TimeProvider.System),
            TimeProvider.System);

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid OwnerId, Guid AdminId);

    private static async Task<Seed> SeedAsync(AtuaDbContext context, bool trialActive,
        EIServiceValidationStatus? credentialStatus, string cnpj = "11122233000183",
        string emailPrefix = "atua")
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", cnpj, "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), $"iService-{emailPrefix}",
            "Fabricante", new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var owner = new User(Guid.CreateVersion7(), null, $"{emailPrefix}.owner@atua.com", "hash");
        var admin = new User(Guid.CreateVersion7(), null, $"{emailPrefix}.admin@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner, admin);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, owner.Id, ETenantMembershipRole.Owner));
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, admin.Id, ETenantMembershipRole.Admin));

        if (trialActive)
        {
            var plan = new Plan(Guid.CreateVersion7(), "trial", "Trial", isFree: true, value: 0m,
                durationDays: 7, maxIntegrations: 2, maxUsers: 5, isActive: true);
            context.Plans.Add(plan);
            context.TenantPlans.Add(new TenantPlan(Guid.CreateVersion7(), tenant.Id, plan.Id,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)));
        }

        if (credentialStatus is not null)
        {
            var credential = new IServiceCredential(Guid.CreateVersion7(), tenant.Id, integration.Id,
                "cipher-user", "cipher-pass", null, [1], [2], "cipher-key", "local-v1", 1,
                DateTimeOffset.UtcNow);
            credential.RecordValidation(credentialStatus.Value, DateTimeOffset.UtcNow);
            context.IServiceCredentials.Add(credential);
        }

        await context.SaveChangesAsync();
        return new Seed(tenant.Id, integration.Id, owner.Id, admin.Id);
    }

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
