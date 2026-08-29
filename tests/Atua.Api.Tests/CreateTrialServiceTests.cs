using Atua.Api.Application.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

public class CreateTrialServiceTests
{
    [Fact]
    public async Task CriaTrialComValidadeSeteDiasAposPicConfirmacaoEmail()
    {
        // Arrange
        var agora = new DateTimeOffset(2026, 8, 29, 13, 12, 30, TimeSpan.Zero);
        var usuarioId = Guid.CreateVersion7();

        await using var context = CreateContext();
        var usuario = new User(usuarioId, null, "owner@atua.com", "hash:senha123");
        usuario.ConfirmEmail(agora);
        context.Users.Add(usuario);
        await context.SaveChangesAsync();

        var servico = CreateService(context, agora);

        // Act
        var resultado = await servico.ExecuteAsync(usuarioId, CancellationToken.None);

        // Assert
        var trial = await context.TrialSubscriptions.FindAsync(new object?[] { resultado.TrialId });
        Assert.NotNull(trial);
        Assert.Equal(usuarioId, trial.UserId);
        Assert.Equal(agora, trial.StartsAt);

        // Expira no início (00:00 UTC) do sétimo dia após a confirmação.
        var expectedExpiry = new DateTimeOffset(agora.UtcDateTime.Date.AddDays(7), TimeSpan.Zero);
        Assert.Equal(expectedExpiry, trial.ExpiresAt);
        Assert.Null(trial.TenantId); // Ainda não associado
    }

    [Fact]
    public async Task CalculaVencimentoPelaDataUtcDaConfirmacao()
    {
        var confirmacao = new DateTimeOffset(2026, 8, 29, 23, 59, 59, TimeSpan.Zero);
        var usuarioId = Guid.CreateVersion7();
        await using var context = CreateContext();
        var usuario = new User(usuarioId, null, "owner@atua.com", "hash:senha123");
        usuario.ConfirmEmail(confirmacao);
        context.Users.Add(usuario);
        await context.SaveChangesAsync();

        var resultado = await CreateService(context, confirmacao)
            .ExecuteAsync(usuarioId, CancellationToken.None);

        Assert.Equal(
            new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero),
            resultado.ExpiresAtUtc);
    }

    [Fact]
    public async Task RejeitaCriarTrialQuandoEmailNaoConfirmado()
    {
        // Arrange
        var usuarioId = Guid.CreateVersion7();
        await using var context = CreateContext();
        var usuario = new User(usuarioId, null, "owner@atua.com", "hash:senha123");
        // Email NÃO confirmado
        context.Users.Add(usuario);
        await context.SaveChangesAsync();

        var servico = CreateService(context, DateTimeOffset.UtcNow);

        // Act & Assert
        var exc = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servico.ExecuteAsync(usuarioId, CancellationToken.None));

        Assert.Contains("E-mail", exc.Message);
    }

    [Fact]
    public async Task RejeitaCriarTrialQuandoUsuarioNaoExiste()
    {
        // Arrange
        var usuarioIdInexistente = Guid.CreateVersion7();
        await using var context = CreateContext();
        var servico = CreateService(context, DateTimeOffset.UtcNow);

        // Act & Assert
        var exc = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servico.ExecuteAsync(usuarioIdInexistente, CancellationToken.None));

        Assert.Contains("Usuário", exc.Message);
    }

    [Fact]
    public async Task RetornaTrialExistenteQuandoJaPossuiUm()
    {
        // Arrange
        var agora = new DateTimeOffset(2026, 8, 29, 13, 12, 30, TimeSpan.Zero);
        var usuarioId = Guid.CreateVersion7();
        var trialIdExistente = Guid.CreateVersion7();

        await using var context = CreateContext();
        var usuario = new User(usuarioId, null, "owner@atua.com", "hash:senha123");
        usuario.ConfirmEmail(agora);
        context.Users.Add(usuario);

        // Criar um Trial existente e ainda ativo
        var trialExistente = new Domain.Billing.TrialSubscription(
            id: trialIdExistente,
            userId: usuarioId,
            startsAt: agora.AddDays(-1),
            expiresAt: agora.AddDays(6)
        );
        context.TrialSubscriptions.Add(trialExistente);
        await context.SaveChangesAsync();

        var servico = CreateService(context, agora);

        // Act
        var resultado = await servico.ExecuteAsync(usuarioId, CancellationToken.None);

        // Assert - retorna o existente (idempotência)
        Assert.Equal(trialIdExistente, resultado.TrialId);

        // Verificar que não foi criado novo Trial
        var countTrials = await context.TrialSubscriptions.CountAsync();
        Assert.Equal(1, countTrials);
    }

    [Fact]
    public async Task RetornaTrialExpiradoExistenteSemReiniciarPrazo()
    {
        var agora = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var usuarioId = Guid.CreateVersion7();
        var trialId = Guid.CreateVersion7();
        await using var context = CreateContext();
        var usuario = new User(usuarioId, null, "owner@atua.com", "hash:senha123");
        usuario.ConfirmEmail(agora.AddDays(-10));
        context.Users.Add(usuario);
        context.TrialSubscriptions.Add(new Domain.Billing.TrialSubscription(
            trialId, usuarioId, agora.AddDays(-10), agora.AddDays(-3)));
        await context.SaveChangesAsync();

        var resultado = await CreateService(context, agora).ExecuteAsync(usuarioId, CancellationToken.None);

        Assert.Equal(trialId, resultado.TrialId);
        Assert.Equal(1, await context.TrialSubscriptions.CountAsync());
    }

    [Fact]
    public async Task AssociaTrialATenantSemAlterarValidade()
    {
        // Arrange
        var agora = new DateTimeOffset(2026, 8, 29, 13, 12, 30, TimeSpan.Zero);
        var usuarioId = Guid.CreateVersion7();
        var trialId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();

        await using var context = CreateContext();

        var trial = new Domain.Billing.TrialSubscription(
            id: trialId,
            userId: usuarioId,
            startsAt: agora,
            expiresAt: agora.AddDays(7)
        );
        context.TrialSubscriptions.Add(trial);
        await context.SaveChangesAsync();

        var servico = CreateService(context, agora);
        var validadeOriginal = trial.ExpiresAt;

        // Act
        await servico.AssociateToTenantAsync(trialId, tenantId, CancellationToken.None);

        // Assert
        var trialAtualizado = await context.TrialSubscriptions.FindAsync(new object?[] { trialId });
        Assert.NotNull(trialAtualizado);
        Assert.Equal(tenantId, trialAtualizado.TenantId);
        Assert.Equal(validadeOriginal, trialAtualizado.ExpiresAt); // Não alterou
    }

    [Fact]
    public async Task RejeitaAssociarTrialJaAssociado()
    {
        // Arrange
        var agora = new DateTimeOffset(2026, 8, 29, 13, 12, 30, TimeSpan.Zero);
        var trialId = Guid.CreateVersion7();
        var tenantId1 = Guid.CreateVersion7();
        var tenantId2 = Guid.CreateVersion7();

        await using var context = CreateContext();

        var trial = new Domain.Billing.TrialSubscription(
            id: trialId,
            userId: Guid.CreateVersion7(),
            startsAt: agora,
            expiresAt: agora.AddDays(7)
        );
        trial.AssociateWithTenant(tenantId1);
        context.TrialSubscriptions.Add(trial);
        await context.SaveChangesAsync();

        var servico = CreateService(context, agora);

        // Act & Assert
        var exc = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servico.AssociateToTenantAsync(trialId, tenantId2, CancellationToken.None));

        Assert.Contains("já foi", exc.Message);
    }

    [Fact]
    public async Task AssociaTrialExpiradoSemAlterarValidade()
    {
        // Arrange
        var agora = new DateTimeOffset(2026, 8, 29, 13, 12, 30, TimeSpan.Zero);
        var trialId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();

        await using var context = CreateContext();

        var trial = new Domain.Billing.TrialSubscription(
            id: trialId,
            userId: Guid.CreateVersion7(),
            startsAt: agora.AddDays(-10),
            expiresAt: agora.AddDays(-3) // Já expirou
        );
        context.TrialSubscriptions.Add(trial);
        await context.SaveChangesAsync();

        var servico = CreateService(context, agora);

        // A expiração bloqueia coleta, não a associação tardia ao Tenant.
        await servico.AssociateToTenantAsync(trialId, tenantId, CancellationToken.None);

        Assert.Equal(tenantId, (await context.TrialSubscriptions.FindAsync(
            new object?[] { trialId }))!.TenantId);
    }

    [Fact]
    public async Task RejeitaAssociarTrialInexistente()
    {
        // Arrange
        var trialIdInexistente = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();

        await using var context = CreateContext();
        var servico = CreateService(context, DateTimeOffset.UtcNow);

        // Act & Assert
        var exc = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servico.AssociateToTenantAsync(trialIdInexistente, tenantId, CancellationToken.None));

        Assert.Contains("não encontrado", exc.Message);
    }

    private static AtuaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AtuaDbContext(options);
    }

    private static CreateTrialService CreateService(AtuaDbContext context, DateTimeOffset now)
    {
        return new CreateTrialService(context, new FixedTimeProvider(now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
