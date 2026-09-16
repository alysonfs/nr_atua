using Atua.Api.Application.Integrations;
using Atua.Api.Application.Integrations.CollectorControl;
using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

public class IServiceCredentialValidationServiceTests
{
    [Fact]
    public async Task RegistraSucceededQuandoAutenticacaoFuncionaERetornaTimestamp()
    {
        await using var context = CreateContext();
        var (tenant, integration, owner) = await SeedAsync(context);
        var cipher = CreateCipher();
        await CreateCredentialService(context, cipher).SetCredentialsAsync(
            owner.Id, tenant.Id, integration.Id, "usuario", "senha-valida", null, CancellationToken.None);

        var service = CreateValidationService(context, cipher, new FakeIServiceAuthClient());
        var result = await service.ValidateAsync(owner.Id, tenant.Id, integration.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.CredentialsConfigured);
        Assert.Equal(EIServiceValidationStatus.Succeeded, result.ValidationStatus);
        Assert.NotNull(result.EvaluatedAtUtc);

        var persisted = await context.IServiceCredentials.SingleAsync();
        Assert.Equal(EIServiceValidationStatus.Succeeded, persisted.ValidationStatus);
        Assert.NotNull(persisted.LastValidatedAtUtc);
    }

    [Fact]
    public async Task RegistraFailedQuandoCredencialInvalidaSegundoOClientFake()
    {
        await using var context = CreateContext();
        var (tenant, integration, owner) = await SeedAsync(context);
        var cipher = CreateCipher();
        await CreateCredentialService(context, cipher).SetCredentialsAsync(
            owner.Id, tenant.Id, integration.Id, "usuario", "invalid", null, CancellationToken.None);

        var service = CreateValidationService(context, cipher, new FakeIServiceAuthClient());
        var result = await service.ValidateAsync(owner.Id, tenant.Id, integration.Id, CancellationToken.None);

        Assert.Equal(EIServiceValidationStatus.Failed, result!.ValidationStatus);
    }

    [Fact]
    public async Task RegistraFailedQuandoClientLancaExcecao()
    {
        await using var context = CreateContext();
        var (tenant, integration, owner) = await SeedAsync(context);
        var cipher = CreateCipher();
        await CreateCredentialService(context, cipher).SetCredentialsAsync(
            owner.Id, tenant.Id, integration.Id, "usuario", "senha-valida", null, CancellationToken.None);

        var service = CreateValidationService(context, cipher, new ThrowingAuthClient());
        var result = await service.ValidateAsync(owner.Id, tenant.Id, integration.Id, CancellationToken.None);

        Assert.Equal(EIServiceValidationStatus.Failed, result!.ValidationStatus);
    }

    [Fact]
    public async Task RetornaNaoConfiguradoQuandoCredenciaisNaoExistem()
    {
        await using var context = CreateContext();
        var (tenant, integration, owner) = await SeedAsync(context);
        var service = CreateValidationService(context, CreateCipher(), new FakeIServiceAuthClient());

        var result = await service.ValidateAsync(owner.Id, tenant.Id, integration.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.CredentialsConfigured);
    }

    /// <summary>
    /// G-2: RF-008.6/ADR-024 — validação com resultado Failed não afeta mais
    /// a elegibilidade (que passou a depender apenas do plano do tenant). O
    /// agente permanece Active.
    /// </summary>
    [Fact]
    public async Task ValidacaoComResultadoFailedNaoDesativaAgente()
    {
        await using var context = CreateContext();
        var (tenant, integration, owner) = await SeedAsync(context);
        var cipher = CreateCipher();

        // Seed: plano ativo.
        var plan = new Plan(Guid.CreateVersion7(), "trial", "Trial", isFree: true, value: 0m,
            durationDays: 7, maxIntegrations: 2, maxUsers: 5, isActive: true);
        context.Plans.Add(plan);
        context.TenantPlans.Add(new TenantPlan(Guid.CreateVersion7(), tenant.Id, plan.Id,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)));
        await context.SaveChangesAsync();

        // Cria credencial com senha válida e força ValidationStatus = Succeeded
        // diretamente (SetCredentialsAsync zeraria o status após gravar).
        await CreateCredentialService(context, cipher).SetCredentialsAsync(
            owner.Id, tenant.Id, integration.Id, "usuario", "senha-valida", null, CancellationToken.None);
        var credential = await context.IServiceCredentials.SingleAsync();
        credential.RecordValidation(EIServiceValidationStatus.Succeeded, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        // Ativa o agente com credencial Succeeded e plano ativo.
        var activationService = CreateCollectorActivationService(context);
        var activationResult = await activationService.ActivateAsync(owner.Id, tenant.Id, integration.Id,
            "chave-ativacao", CancellationToken.None);
        Assert.Equal(ECollectorActivationStatusResult.Success, activationResult.Status);
        Assert.Single(context.ImmediateCollectionCommands);

        // Executa a validação com cliente que retorna Failed.
        var validationService = CreateValidationService(context, cipher, new ThrowingAuthClient());
        await validationService.ValidateAsync(owner.Id, tenant.Id, integration.Id, CancellationToken.None);

        // O plano continua ativo, então o agente permanece Active.
        var activation = await context.CollectorActivations.SingleAsync();
        Assert.Equal(ECollectorActivationStatus.Active, activation.Status);

        var command = await context.ImmediateCollectionCommands.SingleAsync();
        Assert.Equal(EImmediateCollectionCommandStatus.Pending, command.Status);
    }

    [Fact]
    public async Task RetornaNuloQuandoUsuarioNaoEMembro()
    {
        await using var context = CreateContext();
        var (tenant, integration, _) = await SeedAsync(context);
        var outsider = new User(Guid.CreateVersion7(), null, "outsider@atua.com", "hash");
        context.Users.Add(outsider);
        await context.SaveChangesAsync();
        var service = CreateValidationService(context, CreateCipher(), new FakeIServiceAuthClient());

        var result = await service.ValidateAsync(outsider.Id, tenant.Id, integration.Id,
            CancellationToken.None);

        Assert.Null(result);
    }

    private static IServiceCredentialService CreateCredentialService(AtuaDbContext context,
        ICredentialCipher cipher) =>
        new(context, cipher, TimeProvider.System, CreateCollectorActivationService(context));

    private static IServiceCredentialValidationService CreateValidationService(AtuaDbContext context,
        ICredentialCipher cipher, IIServiceAuthClient authClient) =>
        new(context, cipher, authClient, TimeProvider.System,
            CreateCollectorActivationService(context),
            NullLogger<IServiceCredentialValidationService>.Instance);

    /// <summary>
    /// RF-008.6/ADR-020: a validação reconcilia a ativação do coletor na mesma
    /// unidade de trabalho.
    /// </summary>
    private static CollectorActivationService CreateCollectorActivationService(AtuaDbContext context) =>
        new(context,
            new CollectorEligibilityEvaluator(context, TimeProvider.System),
            TimeProvider.System);

    private static async Task<(Tenant Tenant, Atua.Api.Domain.Integrations.Integration Integration,
        User Owner)> SeedAsync(AtuaDbContext context)
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Fabricante X",
            new Uri("https://provider.example.com"), true);
        var integration = new Atua.Api.Domain.Integrations.Integration(Guid.CreateVersion7(), tenant.Id,
            provider.Id, true);
        var owner = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, owner.Id, ETenantMembershipRole.Owner));
        await context.SaveChangesAsync();
        return (tenant, integration, owner);
    }

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ICredentialCipher CreateCipher() => new AesGcmCredentialCipher(Options.Create(
        new CredentialCipherOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(new byte[32])
        }));

    private sealed class ThrowingAuthClient : IIServiceAuthClient
    {
        public Task<IServiceAuthResult> TryAuthenticateAsync(IServiceCredentialPayload credential,
            CancellationToken cancellationToken) => throw new TimeoutException("simulated_timeout");
    }
}
