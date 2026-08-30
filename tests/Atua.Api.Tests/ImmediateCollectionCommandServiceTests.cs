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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

/// <summary>
/// Testes do lado da API para RF-009/ADR-021:
/// - claim devolve credencial decifrada corretamente (username, password via
///   Unpack, baseUrl via Unpack);
/// - expiração de claim por timeout (TryExpireTimeout + job);
/// - invalidação de credencial somente em CredentialRejected (D4).
/// </summary>
public class ImmediateCollectionCommandServiceTests
{
    // -----------------------------------------------------------------------
    // Claim — credencial decifrada
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClaimDevolveCrendencialDecifrada_Username_Password_BaseUrl()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        var (tenant, integration, provider, command) = await SeedWithCommandAsync(context);
        await SeedCredentialAsync(context, cipher, tenant.Id, integration.Id,
            "usuario.test", "senha-secreta", "https://iservice.example.com");

        var service = CreateService(context, cipher);
        var result = await service.ClaimAsync(integration.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(command.Id, result!.CommandId);
        Assert.Equal("usuario.test", result.Credential.Username);
        Assert.Equal("senha-secreta", result.Credential.Password);
        Assert.Equal("https://iservice.example.com", result.Credential.BaseUrl);
        Assert.Equal(integration.Id, result.IntegrationId);
        Assert.Equal(tenant.Id, result.TenantId);
        Assert.NotEqual(default, result.ClaimedAtUtc);
        Assert.NotEqual(default, result.ClaimExpiresAtUtc);
        Assert.Equal(3, result.HistoryWindowMonths); // default

        // Verifica que o comando transitou para Claimed
        var claimed = await context.ImmediateCollectionCommands.SingleAsync();
        Assert.Equal(EImmediateCollectionCommandStatus.Claimed, claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.NotNull(claimed.ClaimExpiresAtUtc);
    }

    [Fact]
    public async Task ClaimDevolveCrendencialDecifrada_SemBaseUrl()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        await SeedWithCommandAsync(context);
        var (_, integration, _, _) = await GetSeedFromContext(context);
        await SeedCredentialAsync(context, cipher, GetTenantId(context), integration.Id,
            "user2", "pass2", null);

        var service = CreateService(context, cipher);
        var result = await service.ClaimAsync(integration.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("user2", result!.Credential.Username);
        Assert.Equal("pass2", result.Credential.Password);
        Assert.Null(result.Credential.BaseUrl);
    }

    [Fact]
    public async Task ClaimRetornaNullQuandoNaoHaComandoPendente()
    {
        await using var context = CreateContext();
        var (_, integration, _, _) = await SeedWithoutCommandAsync(context);

        var service = CreateService(context, CreateCipher());
        var result = await service.ClaimAsync(integration.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ClaimRespeita_HistoryWindowMonths_Configurado()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        await SeedWithCommandAsync(context);
        var (_, integration, _, _) = await GetSeedFromContext(context);
        await SeedCredentialAsync(context, cipher, GetTenantId(context), integration.Id,
            "u", "p", null);

        var service = CreateService(context, cipher, historyWindowMonths: 6);
        var result = await service.ClaimAsync(integration.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(6, result!.HistoryWindowMonths);
    }

    // -----------------------------------------------------------------------
    // Timeout de claim
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryExpireTimeout_TransitaParaFailed_QuandoExpirado()
    {
        var now = DateTimeOffset.UtcNow;
        var command = new ImmediateCollectionCommand(
            Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        // Simula claim com timeout já expirado
        command.TryClaim(now.AddMinutes(-35), TimeSpan.FromMinutes(30));

        var expired = command.TryExpireTimeout(now);

        Assert.True(expired);
        Assert.Equal(EImmediateCollectionCommandStatus.Failed, command.Status);
        Assert.Equal(ECommandFailureReason.ClaimTimeout, command.FailureReason);
        Assert.Equal(ECollectorDeactivationReason.ClaimTimeout, command.CancellationReason);
    }

    [Fact]
    public async Task TryExpireTimeout_NaoExpira_QuandoAindaNoPrazo()
    {
        var now = DateTimeOffset.UtcNow;
        var command = new ImmediateCollectionCommand(
            Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        command.TryClaim(now, TimeSpan.FromMinutes(30));

        var expired = command.TryExpireTimeout(now); // exatamente no limite, não expirado

        Assert.False(expired);
        Assert.Equal(EImmediateCollectionCommandStatus.Claimed, command.Status);
    }

    [Fact]
    public async Task ExpireTimedOutCommandsAsync_ExpiraComandosVencidos()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        var (tenant, integration, provider, command) = await SeedWithCommandAsync(context);
        await SeedCredentialAsync(context, cipher, tenant.Id, integration.Id, "u", "p", null);

        // Faz o claim com tempo passado para que o timeout já tenha ocorrido
        var pastTime = FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-35));
        var servicePast = CreateService(context, cipher, timeProvider: pastTime);
        await servicePast.ClaimAsync(integration.Id, CancellationToken.None);

        // Agora expira usando o serviço com tempo atual
        var serviceNow = CreateService(context, cipher);
        var expired = await serviceNow.ExpireTimedOutCommandsAsync(CancellationToken.None);

        Assert.Equal(1, expired);
        var c = await context.ImmediateCollectionCommands.SingleAsync();
        Assert.Equal(EImmediateCollectionCommandStatus.Failed, c.Status);
        Assert.Equal(ECommandFailureReason.ClaimTimeout, c.FailureReason);
    }

    [Fact]
    public async Task ClaimExpiraComandoTimedOutAntesDeReivindicar()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        var (tenant, integration, provider, command) = await SeedWithCommandAsync(context);
        await SeedCredentialAsync(context, cipher, tenant.Id, integration.Id, "u", "p", null);

        // Faz o claim com tempo passado
        var pastTime = FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-35));
        var servicePast = CreateService(context, cipher, timeProvider: pastTime);
        await servicePast.ClaimAsync(integration.Id, CancellationToken.None);

        // Cria novo comando Pending para a mesma integração
        var newCommand = new ImmediateCollectionCommand(
            Guid.CreateVersion7(), tenant.Id, integration.Id, provider.Id, DateTimeOffset.UtcNow);
        context.ImmediateCollectionCommands.Add(newCommand);
        await context.SaveChangesAsync();

        // Tenta claim com tempo atual — deve expirar o anterior e claimar o novo
        var serviceNow = CreateService(context, cipher);
        var result = await serviceNow.ClaimAsync(integration.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(newCommand.Id, result!.CommandId);

        var commands = await context.ImmediateCollectionCommands.ToListAsync();
        var timedOut = commands.Single(c => c.Id == command.Id);
        var claimed = commands.Single(c => c.Id == newCommand.Id);
        Assert.Equal(EImmediateCollectionCommandStatus.Failed, timedOut.Status);
        Assert.Equal(ECommandFailureReason.ClaimTimeout, timedOut.FailureReason);
        Assert.Equal(EImmediateCollectionCommandStatus.Claimed, claimed.Status);
    }

    // -----------------------------------------------------------------------
    // Complete — invalidação de credencial somente em CredentialRejected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Complete_CredentialRejected_InvalidaValidationStatusEReconciliaElegibilidade()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        var (tenant, integration, provider, command) = await SeedWithCommandAsync(context, trialActive: true);
        await SeedCredentialAsync(context, cipher, tenant.Id, integration.Id, "u", "p", null,
            EIServiceValidationStatus.Succeeded);
        // Precisa de CollectorActivation ativa para a reconciliação fazer efeito
        var activation = new CollectorActivation(Guid.CreateVersion7(), tenant.Id, integration.Id);
        activation.Activate(DateTimeOffset.UtcNow);
        context.CollectorActivations.Add(activation);
        await context.SaveChangesAsync();

        // Claima primeiro
        var service = CreateService(context, cipher);
        await service.ClaimAsync(integration.Id, CancellationToken.None);

        // Complete com CredentialRejected
        var result = await service.CompleteAsync(
            command.Id, integration.Id,
            EImmediateCollectionCommandStatus.Failed,
            ECommandFailureReason.CredentialRejected,
            DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(EImmediateCollectionCommandStatus.Failed, result!.Status);

        var credential = await context.IServiceCredentials.SingleAsync();
        Assert.Equal(EIServiceValidationStatus.Failed, credential.ValidationStatus);

        var act = await context.CollectorActivations.SingleAsync();
        Assert.Equal(ECollectorActivationStatus.Inactive, act.Status);
        Assert.Equal(ECollectorDeactivationReason.CredentialNotValidated, act.DeactivationReason);
    }

    [Fact]
    public async Task Complete_IServiceUnavailable_NaoInvalidaValidationStatus()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        var (tenant, integration, _, command) = await SeedWithCommandAsync(context, trialActive: true);
        await SeedCredentialAsync(context, cipher, tenant.Id, integration.Id, "u", "p", null,
            EIServiceValidationStatus.Succeeded);

        var service = CreateService(context, cipher);
        await service.ClaimAsync(integration.Id, CancellationToken.None);

        var result = await service.CompleteAsync(
            command.Id, integration.Id,
            EImmediateCollectionCommandStatus.Failed,
            ECommandFailureReason.IServiceUnavailable,
            DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(EImmediateCollectionCommandStatus.Failed, result!.Status);

        var credential = await context.IServiceCredentials.SingleAsync();
        // ValidationStatus NÃO deve ter sido alterado
        Assert.Equal(EIServiceValidationStatus.Succeeded, credential.ValidationStatus);
    }

    [Fact]
    public async Task Complete_UnexpectedError_NaoInvalidaValidationStatus()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        var (tenant, integration, _, command) = await SeedWithCommandAsync(context);
        await SeedCredentialAsync(context, cipher, tenant.Id, integration.Id, "u", "p", null,
            EIServiceValidationStatus.Succeeded);

        var service = CreateService(context, cipher);
        await service.ClaimAsync(integration.Id, CancellationToken.None);

        var result = await service.CompleteAsync(
            command.Id, integration.Id,
            EImmediateCollectionCommandStatus.Failed,
            ECommandFailureReason.UnexpectedError,
            DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(result);
        var credential = await context.IServiceCredentials.SingleAsync();
        Assert.Equal(EIServiceValidationStatus.Succeeded, credential.ValidationStatus);
    }

    [Fact]
    public async Task Complete_Succeeded_TransitaParaSucceeded()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        var (tenant, integration, _, command) = await SeedWithCommandAsync(context);
        await SeedCredentialAsync(context, cipher, tenant.Id, integration.Id, "u", "p", null);

        var service = CreateService(context, cipher);
        await service.ClaimAsync(integration.Id, CancellationToken.None);
        var result = await service.CompleteAsync(
            command.Id, integration.Id,
            EImmediateCollectionCommandStatus.Succeeded,
            null,
            DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(EImmediateCollectionCommandStatus.Succeeded, result!.Status);
    }

    [Fact]
    public async Task Complete_Idempotente_JaEmEstadoTerminal()
    {
        await using var context = CreateContext();
        var cipher = CreateCipher();
        var (tenant, integration, _, command) = await SeedWithCommandAsync(context);
        await SeedCredentialAsync(context, cipher, tenant.Id, integration.Id, "u", "p", null);

        var service = CreateService(context, cipher);
        await service.ClaimAsync(integration.Id, CancellationToken.None);
        await service.CompleteAsync(command.Id, integration.Id,
            EImmediateCollectionCommandStatus.Succeeded, null, DateTimeOffset.UtcNow, CancellationToken.None);

        // Segunda chamada idempotente
        var result = await service.CompleteAsync(command.Id, integration.Id,
            EImmediateCollectionCommandStatus.Succeeded, null, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(EImmediateCollectionCommandStatus.Succeeded, result!.Status);
    }

    [Fact]
    public async Task Complete_RetornaNullQuandoCommandIdNaoPertenceAIntegracao()
    {
        await using var context = CreateContext();
        var service = CreateService(context, CreateCipher());

        var result = await service.CompleteAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            EImmediateCollectionCommandStatus.Succeeded,
            null, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Domínio — TryClaim
    // -----------------------------------------------------------------------

    [Fact]
    public void TryClaim_IncrementaAttemptCountEPreencheCampos()
    {
        var now = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromMinutes(30);
        var command = new ImmediateCollectionCommand(
            Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        var claimed = command.TryClaim(now, timeout);

        Assert.True(claimed);
        Assert.Equal(EImmediateCollectionCommandStatus.Claimed, command.Status);
        Assert.Equal(1, command.AttemptCount);
        Assert.Equal(now, command.ClaimedAtUtc);
        Assert.Equal(now.Add(timeout), command.ClaimExpiresAtUtc);
    }

    [Fact]
    public void TryClaim_NaoPermiteClaimDuploSemResetarPendente()
    {
        var now = DateTimeOffset.UtcNow;
        var command = new ImmediateCollectionCommand(
            Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        command.TryClaim(now, TimeSpan.FromMinutes(30));

        // Tentar claim novamente em Claimed deve falhar
        var second = command.TryClaim(now, TimeSpan.FromMinutes(30));

        Assert.False(second);
        Assert.Equal(1, command.AttemptCount);
    }

    // -----------------------------------------------------------------------
    // Concorrência — BUG-009-001
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Claim_RetornaNullSemPropagar_QuandoDbUpdateConcurrencyException()
    {
        // Arrange: seed em banco in-memory compartilhado.
        var dbName = Guid.NewGuid().ToString();
        var cipher = CreateCipher();

        await using var seedContext = CreateContext(dbName);
        var (tenant, integration, _, _) = await SeedWithCommandAsync(seedContext);
        await SeedCredentialAsync(seedContext, cipher, tenant.Id, integration.Id, "u", "p", null);

        // Simula "Worker B": usa DbContext com interceptor que lança
        // DbUpdateConcurrencyException no SaveChangesAsync do claim —
        // representa o Worker que perdeu a corrida (Worker A já persistiu
        // e mudou o ConcurrencyToken antes de Worker B tentar salvar).
        var interceptor = new ConcurrencyOnClaimInterceptor();
        await using var contextB = new AtuaDbContext(
            new DbContextOptionsBuilder<AtuaDbContext>()
                .UseInMemoryDatabase(dbName)
                .AddInterceptors(interceptor)
                .Options);

        var serviceB = CreateService(contextB, cipher);

        // Act
        var result = await serviceB.ClaimAsync(integration.Id, CancellationToken.None);

        // Assert: retorna null (→ 204 No Content), não propaga a exceção (→ 500).
        Assert.Null(result);
    }

    [Fact]
    public async Task Claim_OutrasExcecoesDePersistencia_NaoSaoEngolidas()
    {
        // Garante que somente DbUpdateConcurrencyException é capturada;
        // outras exceções de persistência sobem normalmente.
        var dbName = Guid.NewGuid().ToString();
        var cipher = CreateCipher();

        await using var seedContext = CreateContext(dbName);
        var (tenant, integration, _, _) = await SeedWithCommandAsync(seedContext);
        await SeedCredentialAsync(seedContext, cipher, tenant.Id, integration.Id, "u", "p", null);

        var interceptor = new UnexpectedErrorOnClaimInterceptor();
        await using var contextOther = new AtuaDbContext(
            new DbContextOptionsBuilder<AtuaDbContext>()
                .UseInMemoryDatabase(dbName)
                .AddInterceptors(interceptor)
                .Options);

        var serviceOther = CreateService(contextOther, cipher);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => serviceOther.ClaimAsync(integration.Id, CancellationToken.None));
    }

    private static AtuaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AtuaDbContext CreateContext(string dbName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(dbName).Options);

    private static ICredentialCipher CreateCipher() => new AesGcmCredentialCipher(Options.Create(
        new CredentialCipherOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(new byte[32])
        }));

    private static TimeProvider FakeTimeProvider(DateTimeOffset fixedNow)
    {
        var tp = new FakeTimeProvider(fixedNow);
        return tp;
    }

    private static ImmediateCollectionCommandService CreateService(
        AtuaDbContext context,
        ICredentialCipher cipher,
        int historyWindowMonths = 3,
        TimeProvider? timeProvider = null)
    {
        var tp = timeProvider ?? TimeProvider.System;
        var activationService = new CollectorActivationService(
            context,
            new CollectorEligibilityEvaluator(context,
                new TrialEligibilityService(context, tp)),
            tp);
        return new ImmediateCollectionCommandService(
            context, cipher, activationService, tp,
            Options.Create(new ImmediateCollectionOptions
            {
                HistoryWindowMonths = historyWindowMonths,
                ClaimTimeoutMinutes = 30
            }));
    }

    private static async Task<(Tenant Tenant, Domain.Integrations.Integration Integration,
        IntegrationProvider Provider, ImmediateCollectionCommand Command)>
        SeedWithCommandAsync(AtuaDbContext context, bool trialActive = false)
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Fab",
            new Uri("https://provider.example.com"), true);
        var integration = new Domain.Integrations.Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var owner = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, owner.Id, ETenantMembershipRole.Owner));

        if (trialActive)
        {
            var trial = new TrialSubscription(Guid.CreateVersion7(), owner.Id, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(7));
            trial.AssociateWithTenant(tenant.Id);
            context.TrialSubscriptions.Add(trial);
        }

        var command = new ImmediateCollectionCommand(Guid.CreateVersion7(), tenant.Id, integration.Id,
            provider.Id, DateTimeOffset.UtcNow);
        context.ImmediateCollectionCommands.Add(command);
        await context.SaveChangesAsync();
        return (tenant, integration, provider, command);
    }

    private static async Task<(Tenant Tenant, Domain.Integrations.Integration Integration,
        IntegrationProvider Provider, User Owner)>
        SeedWithoutCommandAsync(AtuaDbContext context)
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "22233344000199", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Fab",
            new Uri("https://provider2.example.com"), true);
        var integration = new Domain.Integrations.Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var owner = new User(Guid.CreateVersion7(), null, "owner2@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, owner.Id, ETenantMembershipRole.Owner));
        await context.SaveChangesAsync();
        return (tenant, integration, provider, owner);
    }

    // Helpers para recuperar entidades após seed (quando seed já foi feito separado)
    private static async Task<(Tenant Tenant, Domain.Integrations.Integration Integration,
        IntegrationProvider Provider, ImmediateCollectionCommand Command)>
        GetSeedFromContext(AtuaDbContext context)
    {
        var command = await context.ImmediateCollectionCommands.FirstAsync();
        var integration = await context.Integrations.FirstAsync(i => i.Id == command.IntegrationId);
        var tenant = await context.Tenants.FirstAsync(t => t.Id == command.TenantId);
        var provider = await context.IntegrationProviders.FirstAsync(p => p.Id == integration.ProviderId);
        return (tenant, integration, provider, command);
    }

    private static Guid GetTenantId(AtuaDbContext context) =>
        context.Tenants.First().Id;

    private static async Task SeedCredentialAsync(
        AtuaDbContext context,
        ICredentialCipher cipher,
        Guid tenantId, Guid integrationId,
        string username, string password, string? baseUrl,
        EIServiceValidationStatus? validationStatus = null)
    {
        var dataKey = await cipher.CreateDataKeyAsync();
        var usernameCipher = cipher.Encrypt(dataKey.Plaintext, username);
        var passwordCipher = cipher.Encrypt(dataKey.Plaintext, password);
        var packedPassword = Pack(passwordCipher);

        string? packedBaseUrl = null;
        if (baseUrl is not null)
        {
            var baseUrlCipher = cipher.Encrypt(dataKey.Plaintext, baseUrl);
            packedBaseUrl = Pack(baseUrlCipher);
        }

        var credential = new IServiceCredential(
            Guid.CreateVersion7(), tenantId, integrationId,
            usernameCipher.CiphertextBase64, packedPassword, packedBaseUrl,
            usernameCipher.Nonce, usernameCipher.Tag,
            dataKey.CiphertextBase64, dataKey.KmsKeyId, dataKey.AlgorithmVersion,
            DateTimeOffset.UtcNow);

        if (validationStatus is not null)
        {
            credential.RecordValidation(validationStatus.Value, DateTimeOffset.UtcNow);
        }

        context.IServiceCredentials.Add(credential);
        await context.SaveChangesAsync();
    }

    private static string Pack(CipherResult cipherResult) =>
        Convert.ToBase64String(cipherResult.Nonce) + "." +
        Convert.ToBase64String(cipherResult.Tag) + "." +
        cipherResult.CiphertextBase64;
}

/// <summary>
/// Implementação mínima de TimeProvider com tempo fixo para testes.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset fixedNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedNow;
}

/// <summary>
/// Interceptor EF Core que lança <see cref="DbUpdateConcurrencyException"/> na
/// primeira chamada a <c>SaveChanges</c> que inclua uma entidade
/// <c>ImmediateCollectionCommand</c> modificada. Simula deterministicamente o
/// Worker que perdeu a corrida de claim (BUG-009-001).
/// </summary>
internal sealed class ConcurrencyOnClaimInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    private bool _triggered;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_triggered || eventData.Context is null) return result;

        var hasClaimedCommand = eventData.Context.ChangeTracker
            .Entries<ImmediateCollectionCommand>()
            .Any(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Modified);

        if (!hasClaimedCommand) return result;

        _triggered = true;
        throw new DbUpdateConcurrencyException(
            "Simulated concurrency conflict: another Worker claimed the command first.");
    }
}

/// <summary>
/// Interceptor EF Core que lança <see cref="InvalidOperationException"/> na
/// primeira chamada a <c>SaveChanges</c> que inclua uma entidade
/// <c>ImmediateCollectionCommand</c> modificada — garante que somente
/// <see cref="DbUpdateConcurrencyException"/> é capturada pelo serviço.
/// </summary>
internal sealed class UnexpectedErrorOnClaimInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    private bool _triggered;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_triggered || eventData.Context is null) return ValueTask.FromResult(result);

        var hasModified = eventData.Context.ChangeTracker
            .Entries<ImmediateCollectionCommand>()
            .Any(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Modified);

        if (!hasModified) return ValueTask.FromResult(result);

        _triggered = true;
        throw new InvalidOperationException("Simulated unexpected persistence error.");
    }
}
