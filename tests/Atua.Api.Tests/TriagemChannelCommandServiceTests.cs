using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de <see cref="TriagemChannelCommandService"/> (ADR-042): claim
/// atômico (sem dupla entrega, isolamento tenant+integração), complete
/// idempotente por outcome (Success/PermanentFailure/TransientFailure),
/// quinta falha transitória consecutiva e lease expirada reclamável.
/// </summary>
public class TriagemChannelCommandServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static (TriagemChannelCommandService Service, FakeTriagemChannelCommandRepository CommandRepository,
        FakeTriagemRepository TriagemRepository) CreateService(DateTimeOffset? now = null, int leaseSeconds = 60)
    {
        var commandRepository = new FakeTriagemChannelCommandRepository();
        var triagemRepository = new FakeTriagemRepository();
        var options = Options.Create(new TriagemChannelOptions { LeaseSeconds = leaseSeconds });
        var service = new TriagemChannelCommandService(commandRepository, triagemRepository,
            new FakeTimeProvider(now ?? Now), options, NullLogger<TriagemChannelCommandService>.Instance);

        return (service, commandRepository, triagemRepository);
    }

    private static async Task<(Guid TriagemId, TriagemChannelCommand Command)> SeedTriagemComComandoAsync(
        FakeTriagemRepository triagemRepository, FakeTriagemChannelCommandRepository commandRepository,
        Guid tenantId, Guid integrationId, DateTimeOffset createdAtUtc)
    {
        var triagemId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var key = new TriagemIdempotencyKey(tenantId, integrationId, $"OS-{Guid.NewGuid()}");
        var candidate = new Triagem(triagemId, tenantId, integrationId, workOrderId, createdAtUtc);
        var command = new TriagemChannelCommand(Guid.NewGuid(), tenantId, integrationId, triagemId, workOrderId,
            "modelo-contato-inicial", 1, key.ToString(), Guid.NewGuid(), createdAtUtc);

        await triagemRepository.GetOrAddWithChannelCommandAsync(key, candidate, command, CancellationToken.None);
        commandRepository.Add(command);

        return (triagemId, command);
    }

    // -----------------------------------------------------------------------
    // Claim
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClaimAsync_SemComandoElegivel_RetornaNull()
    {
        var (service, _, _) = CreateService();

        var result = await service.ClaimAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ClaimAsync_ComComandoPendente_ReivindicaComLeaseEMarcaClaimed()
    {
        var (service, commandRepository, triagemRepository) = CreateService();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (_, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);

        var claimed = await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(command.Id, claimed!.Id);
        Assert.Equal(ETriagemChannelCommandStatus.Claimed, claimed.Status);
        Assert.Equal(Now, claimed.ClaimedAtUtc);
        Assert.Equal(Now.AddSeconds(60), claimed.ClaimExpiresAtUtc);
    }

    [Fact]
    public async Task ClaimAsync_NuncaReivindicaComandoDeOutraTenantOuIntegracao()
    {
        var (service, commandRepository, triagemRepository) = CreateService();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId, integrationId, Now);

        var otherTenant = await service.ClaimAsync(Guid.NewGuid(), integrationId, CancellationToken.None);
        var otherIntegration = await service.ClaimAsync(tenantId, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(otherTenant);
        Assert.Null(otherIntegration);
    }

    [Fact]
    public async Task ClaimAsync_ComandoJaClaimedDentroDaLease_NaoEhReivindicadoNovamente()
    {
        var (service, commandRepository, triagemRepository) = CreateService();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId, integrationId, Now);

        var first = await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);
        var second = await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);

        Assert.NotNull(first);
        // Sem dupla entrega: nenhum outro comando elegível dentro da lease
        // ainda válida.
        Assert.Null(second);
    }

    [Fact]
    public async Task ClaimAsync_LeaseExpirada_TornaOComandoReclamavelPreservandoAChave()
    {
        var (service, commandRepository, triagemRepository) = CreateService(Now, leaseSeconds: 10);
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (_, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);
        var originalKey = command.IdempotencyKey;

        var first = await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);
        Assert.NotNull(first);

        // Avança o relógio além da lease de 10s configurada.
        var afterLeaseService = new TriagemChannelCommandService(commandRepository, triagemRepository,
            new FakeTimeProvider(Now.AddSeconds(11)), Options.Create(new TriagemChannelOptions { LeaseSeconds = 10 }),
            NullLogger<TriagemChannelCommandService>.Instance);

        var reclaimed = await afterLeaseService.ClaimAsync(tenantId, integrationId, CancellationToken.None);

        Assert.NotNull(reclaimed);
        Assert.Equal(command.Id, reclaimed!.Id);
        Assert.Equal(originalKey, reclaimed.IdempotencyKey);
        Assert.Equal(ETriagemChannelCommandStatus.Claimed, reclaimed.Status);
    }

    // -----------------------------------------------------------------------
    // Complete — Success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CompleteAsync_Success_MoveTriagemParaContatoIniciadoEComandoParaCompleted()
    {
        var (service, commandRepository, triagemRepository) = CreateService();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (triagemId, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);
        await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);

        var result = await service.CompleteAsync(command.Id, tenantId, integrationId, command.ClaimedAtUtc!.Value,
            ETriagemChannelSendOutcome.Success, errorCode: null, errorDescription: null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ETriagemChannelCompleteOutcome.Applied, result!.Outcome);
        Assert.Equal(ETriagemChannelCommandStatus.Completed, result.Status);

        var triagem = await triagemRepository.FindByIdAsync(triagemId, CancellationToken.None);
        Assert.NotNull(triagem);
        Assert.Equal(ETriagemStatus.ContatoIniciado, triagem!.Status);
    }

    [Fact]
    public async Task CompleteAsync_Success_ReentregaIdempotenteNaoReaplicaEfeito()
    {
        var (service, commandRepository, triagemRepository) = CreateService();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (triagemId, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);
        await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);
        var first = await service.CompleteAsync(command.Id, tenantId, integrationId, command.ClaimedAtUtc!.Value,
            ETriagemChannelSendOutcome.Success, null, null, CancellationToken.None);

        var second = await service.CompleteAsync(command.Id, tenantId, integrationId, command.ClaimedAtUtc!.Value,
            ETriagemChannelSendOutcome.Success, null, null, CancellationToken.None);

        Assert.Equal(first!.Status, second!.Status);
        Assert.Equal(first.CompletedAtUtc, second.CompletedAtUtc);
        var triagem = await triagemRepository.FindByIdAsync(triagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.ContatoIniciado, triagem!.Status);
    }

    // -----------------------------------------------------------------------
    // Complete — PermanentFailure
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CompleteAsync_PermanentFailure_MoveTriagemParaFalhaDeContato()
    {
        var (service, commandRepository, triagemRepository) = CreateService();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (triagemId, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);
        await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);

        var result = await service.CompleteAsync(command.Id, tenantId, integrationId, command.ClaimedAtUtc!.Value,
            ETriagemChannelSendOutcome.PermanentFailure, "numero-invalido", "Número de contato inválido.",
            CancellationToken.None);

        Assert.Equal(ETriagemChannelCommandStatus.Failed, result!.Status);
        var triagem = await triagemRepository.FindByIdAsync(triagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.FalhaDeContato, triagem!.Status);
    }

    // -----------------------------------------------------------------------
    // Complete — TransientFailure / replay / quinta falha
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CompleteAsync_TransientFailure_AbaixoDoLimite_VoltaParaPendingSemAlterarTriagem()
    {
        var (service, commandRepository, triagemRepository) = CreateService();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (triagemId, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);
        await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);

        var result = await service.CompleteAsync(command.Id, tenantId, integrationId, command.ClaimedAtUtc!.Value,
            ETriagemChannelSendOutcome.TransientFailure, "timeout-provedor", "Tempo esgotado.",
            CancellationToken.None);

        Assert.Equal(ETriagemChannelCommandStatus.Pending, result!.Status);
        var triagem = await triagemRepository.FindByIdAsync(triagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, triagem!.Status);
    }

    [Fact]
    public async Task CompleteAsync_CincoFalhasTransitoriasConsecutivas_MoveTriagemParaFalhaDeContato()
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (service, commandRepository, triagemRepository) = CreateService();
        var (triagemId, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);

        for (var attempt = 1; attempt <= TriagemChannelCommand.MaxTransientFailures; attempt++)
        {
            var claimed = await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);
            Assert.NotNull(claimed);

            var result = await service.CompleteAsync(command.Id, tenantId, integrationId,
                claimed!.ClaimedAtUtc!.Value, ETriagemChannelSendOutcome.TransientFailure, "timeout-provedor",
                "Tempo esgotado.", CancellationToken.None);

            if (attempt < TriagemChannelCommand.MaxTransientFailures)
            {
                Assert.Equal(ETriagemChannelCommandStatus.Pending, result!.Status);
            }
            else
            {
                Assert.Equal(ETriagemChannelCommandStatus.Failed, result!.Status);
            }
        }

        var triagem = await triagemRepository.FindByIdAsync(triagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.FalhaDeContato, triagem!.Status);
    }

    [Fact]
    public async Task CompleteAsync_FalhaTransitoriaSeguidaDeRetomada_ReclamaComMesmaChaveEConcluiComSucesso()
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (service, commandRepository, triagemRepository) = CreateService();
        var (triagemId, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);
        var originalKey = command.IdempotencyKey;

        await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);
        await service.CompleteAsync(command.Id, tenantId, integrationId, command.ClaimedAtUtc!.Value,
            ETriagemChannelSendOutcome.TransientFailure, "timeout-provedor", "Tempo esgotado.", CancellationToken.None);

        var reclaimed = await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);
        Assert.NotNull(reclaimed);
        Assert.Equal(originalKey, reclaimed!.IdempotencyKey);

        var result = await service.CompleteAsync(command.Id, tenantId, integrationId, reclaimed.ClaimedAtUtc!.Value,
            ETriagemChannelSendOutcome.Success, null, null, CancellationToken.None);

        Assert.Equal(ETriagemChannelCommandStatus.Completed, result!.Status);
        var triagem = await triagemRepository.FindByIdAsync(triagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.ContatoIniciado, triagem!.Status);
    }

    // -----------------------------------------------------------------------
    // Isolamento tenant+integração / não encontrado / lease perdida
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CompleteAsync_ComandoDeOutraTenantOuIntegracao_RetornaNull()
    {
        var (service, commandRepository, triagemRepository) = CreateService();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (_, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);
        await service.ClaimAsync(tenantId, integrationId, CancellationToken.None);

        var byOtherTenant = await service.CompleteAsync(command.Id, Guid.NewGuid(), integrationId,
            command.ClaimedAtUtc!.Value, ETriagemChannelSendOutcome.Success, null, null, CancellationToken.None);
        var byOtherIntegration = await service.CompleteAsync(command.Id, tenantId, Guid.NewGuid(),
            command.ClaimedAtUtc!.Value, ETriagemChannelSendOutcome.Success, null, null, CancellationToken.None);

        Assert.Null(byOtherTenant);
        Assert.Null(byOtherIntegration);
    }

    [Fact]
    public async Task CompleteAsync_CommandIdInexistente_RetornaNull()
    {
        var (service, _, _) = CreateService();

        var result = await service.CompleteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now,
            ETriagemChannelSendOutcome.Success, null, null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CompleteAsync_ComandoAindaPendingSemClaim_NaoAplicaEfeitoEDevolveEstadoAtual()
    {
        // Complete recebido para um comando que nunca foi reivindicado
        // (ou cuja lease já expirou e voltou a Pending) nunca força a
        // transição — outro chamador pode ter reivindicado o comando nesse
        // meio-tempo. Sem ClaimedAtUtc vigente (comando Pending), o
        // resultado é LeaseLost.
        var (service, commandRepository, triagemRepository) = CreateService();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (triagemId, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);

        var result = await service.CompleteAsync(command.Id, tenantId, integrationId, Now,
            ETriagemChannelSendOutcome.Success, null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ETriagemChannelCompleteOutcome.LeaseLost, result!.Outcome);
        Assert.Equal(ETriagemChannelCommandStatus.Pending, result.Status);
        var triagem = await triagemRepository.FindByIdAsync(triagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, triagem!.Status);
    }

    [Fact]
    public async Task CompleteAsync_LeaseExpiradaSubstituidaPorOutroClaim_TitularAntigoRecebeLeaseLost_NovoTitularConclui()
    {
        // Cenário corrigido (ADR-042, correção de concorrência): claim A
        // expira, claim B reivindica o mesmo comando (nova lease, novo
        // token). O complete do titular antigo (A) nunca deve aplicar
        // efeito — apenas o complete do titular vigente (B) pode concluir.
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var (serviceAtClaimA, commandRepository, triagemRepository) = CreateService(Now, leaseSeconds: 10);
        var (triagemId, command) = await SeedTriagemComComandoAsync(triagemRepository, commandRepository, tenantId,
            integrationId, Now);

        var claimA = await serviceAtClaimA.ClaimAsync(tenantId, integrationId, CancellationToken.None);
        Assert.NotNull(claimA);
        var tokenA = claimA!.ClaimedAtUtc!.Value;

        // Avança o relógio além da lease de A (10s) e reivindica de novo —
        // simula uma segunda instância do canal (B) assumindo o comando.
        var afterLeaseExpiryNow = Now.AddSeconds(11);
        var serviceAtClaimB = new TriagemChannelCommandService(commandRepository, triagemRepository,
            new FakeTimeProvider(afterLeaseExpiryNow),
            Options.Create(new TriagemChannelOptions { LeaseSeconds = 10 }),
            NullLogger<TriagemChannelCommandService>.Instance);
        var claimB = await serviceAtClaimB.ClaimAsync(tenantId, integrationId, CancellationToken.None);
        Assert.NotNull(claimB);
        var tokenB = claimB!.ClaimedAtUtc!.Value;
        Assert.NotEqual(tokenA, tokenB);

        // Complete de A (titular antigo, token obsoleto): lease perdida —
        // nenhum efeito aplicado.
        var resultFromA = await serviceAtClaimB.CompleteAsync(command.Id, tenantId, integrationId, tokenA,
            ETriagemChannelSendOutcome.Success, null, null, CancellationToken.None);

        Assert.NotNull(resultFromA);
        Assert.Equal(ETriagemChannelCompleteOutcome.LeaseLost, resultFromA!.Outcome);
        Assert.Equal(ETriagemChannelCommandStatus.Claimed, resultFromA.Status);
        var triagemAposA = await triagemRepository.FindByIdAsync(triagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, triagemAposA!.Status);

        // Complete de B (titular vigente, token correto): aplica o efeito
        // normalmente.
        var resultFromB = await serviceAtClaimB.CompleteAsync(command.Id, tenantId, integrationId, tokenB,
            ETriagemChannelSendOutcome.Success, null, null, CancellationToken.None);

        Assert.NotNull(resultFromB);
        Assert.Equal(ETriagemChannelCompleteOutcome.Applied, resultFromB!.Outcome);
        Assert.Equal(ETriagemChannelCommandStatus.Completed, resultFromB.Status);
        var triagemAposB = await triagemRepository.FindByIdAsync(triagemId, CancellationToken.None);
        Assert.Equal(ETriagemStatus.ContatoIniciado, triagemAposB!.Status);
    }
}
