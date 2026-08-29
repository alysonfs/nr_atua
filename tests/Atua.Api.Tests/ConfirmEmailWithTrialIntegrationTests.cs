using Atua.Api.Application.Billing;
using Atua.Api.Application.Identity;
using Atua.Api.Domain.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de integração para validar CA-2:
/// "Dado um e-mail confirmado, quando a conta for ativada, entao o cliente deve
/// receber Trial valido ate o fim do setimo dia contado em UTC."
/// </summary>
public class ConfirmEmailWithTrialIntegrationTests
{
    [Fact]
    public async Task CA2_CriaTrialAoConfirmarEmail()
    {
        // Arrange
        var registeredAt = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var confirmAt = registeredAt.AddMinutes(5);
        var usuarioId = Guid.CreateVersion7();

        await using var context = CreateContext();
        var usuario = new User(usuarioId, null, "owner@atua.com", "hash:senha123");
        context.Users.Add(usuario);

        var confirmation = new EmailConfirmation(
            Guid.CreateVersion7(),
            usuarioId,
            "hash:482913",
            registeredAt.AddMinutes(15)
        );
        context.EmailConfirmations.Add(confirmation);
        await context.SaveChangesAsync();

        var emailConfirmService = CreateConfirmEmailService(context, confirmAt);
        var command = new ConfirmEmailCommand("OWNER@atua.com", "482913");

        // Act
        var result = await emailConfirmService.ExecuteAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(EConfirmEmailStatus.Success, result.Status);

        // Verificar que Trial foi criado automaticamente
        var trial = await context.TrialSubscriptions
            .Where(t => t.UserId == usuarioId)
            .FirstOrDefaultAsync();

        Assert.NotNull(trial);
        Assert.Equal(usuarioId, trial.UserId);
        Assert.Null(trial.TenantId); // Ainda não associado a tenant

        var expectedExpiry = new DateTimeOffset(confirmAt.UtcDateTime.Date.AddDays(7), TimeSpan.Zero);
        Assert.Equal(expectedExpiry, trial.ExpiresAt);
    }

    [Fact]
    public async Task CA3_AssociaTrialATenantSemAlterarValidade()
    {
        // Arrange
        var confirmAt = new DateTimeOffset(2026, 8, 29, 13, 12, 30, TimeSpan.Zero);
        var usuarioId = Guid.CreateVersion7();
        var trialId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();

        await using var context = CreateContext();

        // Criar usuário com Trial já existente
        var usuario = new User(usuarioId, null, "owner@atua.com", "hash:senha123");
        usuario.ConfirmEmail(confirmAt.AddDays(-1));
        context.Users.Add(usuario);

        var trial = new Domain.Billing.TrialSubscription(
            id: trialId,
            userId: usuarioId,
            startsAt: confirmAt.AddDays(-1),
            expiresAt: confirmAt.AddDays(6)
        );
        context.TrialSubscriptions.Add(trial);
        await context.SaveChangesAsync();

        var validadeOriginal = trial.ExpiresAt;
        var createTrialService = new CreateTrialService(context, new FixedTimeProvider(confirmAt));

        // Act
        await createTrialService.AssociateToTenantAsync(trialId, tenantId, CancellationToken.None);

        // Assert
        var trialAtualizado = await context.TrialSubscriptions.FindAsync(
            new object?[] { trialId });

        Assert.NotNull(trialAtualizado);
        Assert.Equal(tenantId, trialAtualizado.TenantId);
        Assert.Equal(validadeOriginal, trialAtualizado.ExpiresAt); // Sem reiniciar
    }

    private static AtuaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AtuaDbContext(options);
    }

    private static ConfirmEmailService CreateConfirmEmailService(
        AtuaDbContext context,
        DateTimeOffset now)
    {
        var createTrialService = new CreateTrialService(context, new FixedTimeProvider(now));
        return new ConfirmEmailService(
            context,
            new FakeSecretHasher(),
            new FixedTimeProvider(now),
            createTrialService
        );
    }

    private sealed class FakeSecretHasher : ISecretHasher
    {
        public string Hash(string value) => $"hash:{value}";

        public bool Verify(string value, string hash) => hash == Hash(value);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
