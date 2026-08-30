using Atua.Api.Application.Billing;
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
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

public class IServiceCredentialServiceTests
{
    [Fact]
    public async Task OwnerConsegueCriarCredenciaisENuncaExpoeSegredoNaLeitura()
    {
        await using var context = CreateContext();
        var (tenant, integration, owner, _) = await SeedAsync(context);
        var service = CreateCredentialService(context, CreateCipher(), FixedTimeProvider(context));

        var status = await service.SetCredentialsAsync(owner.Id, tenant.Id, integration.Id,
            "usuario.iservice", "senha-secreta", "https://iservice.example.com", CancellationToken.None);

        Assert.Equal(ESetCredentialsStatus.Success, status);

        var result = await service.GetStatusAsync(owner.Id, tenant.Id, integration.Id, CancellationToken.None);
        Assert.NotNull(result);
        Assert.True(result!.HasCredentials);
        Assert.Equal(EIServiceValidationStatus.NotValidated, result.ValidationStatus);

        var persisted = await context.IServiceCredentials.SingleAsync();
        Assert.DoesNotContain("usuario.iservice", persisted.UsernameCiphertext);
        Assert.DoesNotContain("senha-secreta", persisted.PasswordCiphertext);
    }

    [Fact]
    public async Task MembroNaoOwnerNaoConsegueEscreverCredenciais()
    {
        await using var context = CreateContext();
        var (tenant, integration, _, admin) = await SeedAsync(context);
        var service = CreateCredentialService(context, CreateCipher(), FixedTimeProvider(context));

        var status = await service.SetCredentialsAsync(admin.Id, tenant.Id, integration.Id,
            "usuario", "senha", null, CancellationToken.None);

        Assert.Equal(ESetCredentialsStatus.Forbidden, status);
        Assert.Empty(context.IServiceCredentials);
    }

    [Fact]
    public async Task QualquerMembroConsegueLerStatusDeCredenciais()
    {
        await using var context = CreateContext();
        var (tenant, integration, owner, admin) = await SeedAsync(context);
        var service = CreateCredentialService(context, CreateCipher(), FixedTimeProvider(context));
        await service.SetCredentialsAsync(owner.Id, tenant.Id, integration.Id, "usuario", "senha", null,
            CancellationToken.None);

        var result = await service.GetStatusAsync(admin.Id, tenant.Id, integration.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.HasCredentials);
    }

    [Fact]
    public async Task AlterarCredenciaisResetaValidationStatusParaNotValidated()
    {
        await using var context = CreateContext();
        var (tenant, integration, owner, _) = await SeedAsync(context);
        var service = CreateCredentialService(context, CreateCipher(), FixedTimeProvider(context));
        await service.SetCredentialsAsync(owner.Id, tenant.Id, integration.Id, "usuario", "senha", null,
            CancellationToken.None);

        var credential = await context.IServiceCredentials.SingleAsync();
        credential.RecordValidation(EIServiceValidationStatus.Succeeded, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        await service.SetCredentialsAsync(owner.Id, tenant.Id, integration.Id, "usuario2", "senha2", null,
            CancellationToken.None);

        var reloaded = await context.IServiceCredentials.SingleAsync();
        Assert.Equal(EIServiceValidationStatus.NotValidated, reloaded.ValidationStatus);
        Assert.Null(reloaded.LastValidatedAtUtc);
    }

    /// <summary>
    /// G-1: RF-008.6/ADR-020 — trocar credencial (SetCredentialsAsync) zera o
    /// ValidationStatus, tornando a elegibilidade falsa. O agente deve ser
    /// desativado e o comando Pending cancelado na mesma unidade de trabalho.
    /// </summary>
    [Fact]
    public async Task TrocarCredencialDesativaAgenteECancelaComandoPendente()
    {
        await using var context = CreateContext();
        var (tenant, integration, owner, _) = await SeedAsync(context);
        var cipher = CreateCipher();

        // Seed: credencial com Succeeded e trial ativo para que a ativação funcione.
        var trial = new Atua.Api.Domain.Billing.TrialSubscription(Guid.CreateVersion7(), owner.Id,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
        trial.AssociateWithTenant(tenant.Id);
        context.TrialSubscriptions.Add(trial);
        var credential = new Atua.Api.Domain.Integrations.IServiceCredential(Guid.CreateVersion7(),
            tenant.Id, integration.Id, "cipher-user", "cipher-pass", null, [1], [2],
            "cipher-key", "local-v1", 1, DateTimeOffset.UtcNow);
        credential.RecordValidation(EIServiceValidationStatus.Succeeded, DateTimeOffset.UtcNow);
        context.IServiceCredentials.Add(credential);
        await context.SaveChangesAsync();

        // Ativa o agente.
        var activationService = CreateCollectorActivationService(context);
        var activationResult = await activationService.ActivateAsync(owner.Id, tenant.Id, integration.Id,
            "chave-ativacao", CancellationToken.None);
        Assert.Equal(ECollectorActivationStatusResult.Success, activationResult.Status);
        Assert.Equal("Active", activationResult.View!.Status);
        Assert.Single(context.ImmediateCollectionCommands);

        // Substitui a credencial: ValidationStatus volta a NotValidated.
        var credentialService = CreateCredentialService(context, cipher, FixedTimeProvider(context));
        var setStatus = await credentialService.SetCredentialsAsync(owner.Id, tenant.Id, integration.Id,
            "novo-usuario", "nova-senha", null, CancellationToken.None);
        Assert.Equal(ESetCredentialsStatus.Success, setStatus);

        // Agente deve estar Inactive e o comando deve ter sido cancelado com
        // motivo CredentialNotValidated (RF-008.6).
        var activation = await context.CollectorActivations.SingleAsync();
        Assert.Equal(ECollectorActivationStatus.Inactive, activation.Status);
        Assert.Equal(ECollectorDeactivationReason.CredentialNotValidated, activation.DeactivationReason);

        var command = await context.ImmediateCollectionCommands.SingleAsync();
        Assert.Equal(EImmediateCollectionCommandStatus.Cancelled, command.Status);
        Assert.Equal(ECollectorDeactivationReason.CredentialNotValidated, command.CancellationReason);
    }

    [Fact]
    public async Task UsuarioForaDoTenantNaoConsegueLerStatus()
    {
        await using var context = CreateContext();
        var (tenant, integration, _, _) = await SeedAsync(context);
        var outsider = new User(Guid.CreateVersion7(), null, "outsider@atua.com", "hash");
        context.Users.Add(outsider);
        await context.SaveChangesAsync();
        var service = CreateCredentialService(context, CreateCipher(), FixedTimeProvider(context));

        var result = await service.GetStatusAsync(outsider.Id, tenant.Id, integration.Id,
            CancellationToken.None);

        Assert.Null(result);
    }

    private static async Task<(Tenant Tenant, Atua.Api.Domain.Integrations.Integration Integration,
        User Owner, User Admin)> SeedAsync(AtuaDbContext context)
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Fabricante X",
            new Uri("https://provider.example.com"), true);
        var integration = new Atua.Api.Domain.Integrations.Integration(Guid.CreateVersion7(), tenant.Id,
            provider.Id, true);
        var owner = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        var admin = new User(Guid.CreateVersion7(), null, "admin@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner, admin);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, owner.Id, ETenantMembershipRole.Owner));
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, admin.Id, ETenantMembershipRole.Admin));
        await context.SaveChangesAsync();
        return (tenant, integration, owner, admin);
    }

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ICredentialCipher CreateCipher() => new AesGcmCredentialCipher(Options.Create(
        new CredentialCipherOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(new byte[32])
        }));

    private static TimeProvider FixedTimeProvider(AtuaDbContext _) => TimeProvider.System;

    private static IServiceCredentialService CreateCredentialService(AtuaDbContext context,
        ICredentialCipher cipher, TimeProvider timeProvider) =>
        new(context, cipher, timeProvider, CreateCollectorActivationService(context));

    /// <summary>
    /// RF-008.6/ADR-020: gravar credencial reconcilia a ativação do coletor na
    /// mesma unidade de trabalho.
    /// </summary>
    private static CollectorActivationService CreateCollectorActivationService(AtuaDbContext context) =>
        new(context,
            new CollectorEligibilityEvaluator(context,
                new TrialEligibilityService(context, TimeProvider.System)),
            TimeProvider.System);
}
