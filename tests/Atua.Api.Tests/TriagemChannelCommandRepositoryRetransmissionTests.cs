using Atua.Api.Application.Triagens;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de <see cref="TriagemChannelCommandRepository.ListCompletedSinceAsync"/>
/// focados no limite de candidatos (<see cref="TriagemChannelCommandRepository.MaxRetransmissionCandidates"/>,
/// 256 — mesma ordem de grandeza do cache em memória do canal,
/// <c>MAX_OUTBOUND_RETRY_MESSAGES</c>) e no isolamento por tenant/integração,
/// usados pela retransmissão reativa de <c>getMessage</c> (Opção A — sem
/// migration, varredura por hash dentro do limite/janela).
/// </summary>
public class TriagemChannelCommandRepositoryRetransmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListCompletedSinceAsync_AcimaDoLimite_RetornaApenasOsMaisRecentes()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        const int totalCommands = TriagemChannelCommandRepository.MaxRetransmissionCandidates + 10;

        await using (var seedContext = CreateContext(databaseName))
        {
            for (var i = 0; i < totalCommands; i++)
            {
                var completedAt = Now.AddMinutes(-i); // i=0 é o mais recente.
                seedContext.Add(CreateCompletedCommand(tenantId, integrationId, $"idemp-{i}", completedAt));
            }

            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName);
        var repository = new TriagemChannelCommandRepository(context);

        var result = await repository.ListCompletedSinceAsync(tenantId, integrationId,
            Now.AddHours(-1), CancellationToken.None);

        Assert.Equal(TriagemChannelCommandRepository.MaxRetransmissionCandidates, result.Count);
        // Os 10 mais antigos (i = totalCommands-10 .. totalCommands-1) devem ficar de fora.
        var returnedKeys = result.Select(command => command.IdempotencyKey).ToHashSet();
        for (var i = 0; i < TriagemChannelCommandRepository.MaxRetransmissionCandidates; i++)
        {
            Assert.Contains($"idemp-{i}", returnedKeys);
        }

        for (var i = TriagemChannelCommandRepository.MaxRetransmissionCandidates; i < totalCommands; i++)
        {
            Assert.DoesNotContain($"idemp-{i}", returnedKeys);
        }
    }

    [Fact]
    public async Task ListCompletedSinceAsync_IsolaPorTenantEIntegracao()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();

        await using (var seedContext = CreateContext(databaseName))
        {
            seedContext.Add(CreateCompletedCommand(tenantId, integrationId, "idemp-same-tenant", Now));
            seedContext.Add(CreateCompletedCommand(Guid.NewGuid(), integrationId, "idemp-other-tenant", Now));
            seedContext.Add(CreateCompletedCommand(tenantId, Guid.NewGuid(), "idemp-other-integration", Now));
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName);
        var repository = new TriagemChannelCommandRepository(context);

        var result = await repository.ListCompletedSinceAsync(tenantId, integrationId,
            Now.AddHours(-1), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("idemp-same-tenant", result[0].IdempotencyKey);
    }

    [Fact]
    public async Task ListCompletedSinceAsync_ForaDaJanela_NaoRetornaComando()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();

        await using (var seedContext = CreateContext(databaseName))
        {
            seedContext.Add(CreateCompletedCommand(tenantId, integrationId, "idemp-antigo",
                Now.AddHours(-2)));
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName);
        var repository = new TriagemChannelCommandRepository(context);

        var result = await repository.ListCompletedSinceAsync(tenantId, integrationId,
            Now.AddHours(-1), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListCompletedSinceAsync_ComandoNaoConcluido_NaoRetornaComando()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();

        await using (var seedContext = CreateContext(databaseName))
        {
            var command = new TriagemChannelCommand(Guid.NewGuid(), tenantId, integrationId, Guid.NewGuid(),
                Guid.NewGuid(), "template-version-id", 1, "conteúdo renderizado", "idemp-pendente",
                Guid.NewGuid(), Now, recipientPhone: "+5511999999999");
            seedContext.Add(command);
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName);
        var repository = new TriagemChannelCommandRepository(context);

        var result = await repository.ListCompletedSinceAsync(tenantId, integrationId,
            Now.AddHours(-1), CancellationToken.None);

        Assert.Empty(result);
    }

    private static TriagemChannelCommand CreateCompletedCommand(Guid tenantId, Guid integrationId,
        string idempotencyKey, DateTimeOffset completedAtUtc)
    {
        var command = new TriagemChannelCommand(Guid.NewGuid(), tenantId, integrationId, Guid.NewGuid(),
            Guid.NewGuid(), "template-version-id", 1, "conteúdo renderizado", idempotencyKey,
            Guid.NewGuid(), Now, recipientPhone: "+5511999999999");
        command.Claim(Now, Now.AddSeconds(60));
        command.MarkCompletedSuccess(completedAtUtc);
        return command;
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
}
