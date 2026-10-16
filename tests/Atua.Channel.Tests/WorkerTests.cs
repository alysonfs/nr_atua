using Atua.Channel.Api;
using Atua.Channel.Configuration;
using Atua.Channel.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Atua.Channel.Tests;

/// <summary>
/// Testes do loop do <see cref="Worker"/> (ADR-042): claim → complete
/// (sucesso fake) → tenta novo claim imediatamente (throughput); sem
/// comando elegível → aguarda o intervalo de polling; lease perdida (409 —
/// complete retorna <c>null</c>) → também tenta novo claim imediatamente;
/// exceção inesperada → loga e continua o loop sem derrubar o processo.
/// Usa <see cref="IChannelApiClient"/> substituído (NSubstitute) — nenhuma
/// chamada HTTP real.
/// </summary>
public class WorkerTests
{
    [Fact]
    public async Task QuandoHaComandoElegivel_ReivindicaEConcluiComSucessoFake()
    {
        var apiClient = Substitute.For<IChannelApiClient>();
        var command = new ClaimChannelCommandResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "template-inicial", 1,
            "tenant:integration:OS-1", Guid.NewGuid(), 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(60));

        var callCount = 0;
        apiClient.ClaimAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            return callCount == 1 ? command : null;
        });
        apiClient.CompleteAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteChannelCommandResponse(command.CommandId, "Completed", DateTimeOffset.UtcNow));

        var worker = CreateWorker(apiClient, pollingIntervalSeconds: 60);

        await RunOneCycleAsync(worker);

        await apiClient.Received(1).CompleteAsync(command.CommandId, command.ClaimedAtUtc, "Success", null, null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AposCompleteComSucesso_TentaNovoClaimImediatamenteSemAguardarOIntervalo()
    {
        // Throughput (ADR-042): o worker não deve pagar o intervalo de
        // polling depois de um comando concluído — apenas quando não há
        // trabalho (204) ou em caso de erro.
        var apiClient = Substitute.For<IChannelApiClient>();
        var firstCommand = new ClaimChannelCommandResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "template-inicial", 1,
            "tenant:integration:OS-1", Guid.NewGuid(), 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(60));
        var secondCommand = new ClaimChannelCommandResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "template-inicial", 1,
            "tenant:integration:OS-2", Guid.NewGuid(), 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(60));

        var claimCallCount = 0;
        apiClient.ClaimAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            claimCallCount++;
            return claimCallCount switch
            {
                1 => firstCommand,
                2 => secondCommand,
                _ => null,
            };
        });
        apiClient.CompleteAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => new CompleteChannelCommandResponse((Guid)call[0], "Completed", DateTimeOffset.UtcNow));

        // Intervalo de polling grande: se o worker aguardasse entre ciclos,
        // o segundo claim jamais ocorreria dentro da janela do teste.
        var worker = CreateWorker(apiClient, pollingIntervalSeconds: 60);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await worker.StopAsync(CancellationToken.None);

        await apiClient.Received(1).CompleteAsync(firstCommand.CommandId, firstCommand.ClaimedAtUtc, "Success", null,
            null, Arg.Any<CancellationToken>());
        await apiClient.Received(1).CompleteAsync(secondCommand.CommandId, secondCommand.ClaimedAtUtc, "Success",
            null, null, Arg.Any<CancellationToken>());
        Assert.True(claimCallCount >= 3);
    }

    [Fact]
    public async Task QuandoCompleteRetornaNull_LeasePerdida_TentaNovoClaimImediatamenteSemAguardar()
    {
        // Lease perdida (409 → complete retorna null): outra instância já
        // reivindicou o comando. O worker nunca trata isso como erro —
        // tenta um novo claim imediatamente, igual ao caminho de sucesso.
        var apiClient = Substitute.For<IChannelApiClient>();
        var command = new ClaimChannelCommandResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "template-inicial", 1,
            "tenant:integration:OS-1", Guid.NewGuid(), 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(60));

        var claimCallCount = 0;
        apiClient.ClaimAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            claimCallCount++;
            return claimCallCount == 1 ? command : null;
        });
        apiClient.CompleteAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((CompleteChannelCommandResponse?)null);

        var worker = CreateWorker(apiClient, pollingIntervalSeconds: 60);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await worker.StopAsync(CancellationToken.None);

        // Mais de um claim dentro da janela do teste prova que o worker não
        // aguardou o intervalo de polling (60s) após a lease perdida.
        Assert.True(claimCallCount >= 2);
    }

    [Fact]
    public async Task QuandoNaoHaComandoElegivel_NaoChamaComplete()
    {
        var apiClient = Substitute.For<IChannelApiClient>();
        apiClient.ClaimAsync(Arg.Any<CancellationToken>()).Returns((ClaimChannelCommandResponse?)null);

        var worker = CreateWorker(apiClient, pollingIntervalSeconds: 60);

        await RunOneCycleAsync(worker);

        await apiClient.DidNotReceive().CompleteAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuandoClaimLancaExcecao_WorkerContinuaRodandoSemDerrubarOProcesso()
    {
        var apiClient = Substitute.For<IChannelApiClient>();
        apiClient.ClaimAsync(Arg.Any<CancellationToken>())
            .Returns<Task<ClaimChannelCommandResponse?>>(_ => throw new HttpRequestException("falha simulada"));

        var worker = CreateWorker(apiClient, pollingIntervalSeconds: 60);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await worker.StopAsync(CancellationToken.None);

        // Não propagou exceção — o processo continuou de pé (BackgroundService
        // não derrubou a aplicação) e tentou reivindicar ao menos uma vez.
        await apiClient.Received().ClaimAsync(Arg.Any<CancellationToken>());
    }

    private static Atua.Channel.Worker CreateWorker(IChannelApiClient apiClient, int pollingIntervalSeconds) =>
        new(apiClient,
            Options.Create(new ChannelWorkerOptions
            {
                ApiBaseUrl = "http://localhost:5240",
                ClaimServiceToken = "t",
                CompleteServiceToken = "t",
                PollingIntervalSeconds = pollingIntervalSeconds,
            }),
            NullLogger<Atua.Channel.Worker>.Instance);

    /// <summary>
    /// Executa exatamente um ciclo do worker (claim [+ complete]) iniciando e
    /// parando o <see cref="BackgroundService"/> rapidamente — o intervalo de
    /// polling configurado (60s) garante que o <c>Task.Delay</c> do fim do
    /// ciclo não seja concluído antes do <c>StopAsync</c>, evitando um
    /// segundo ciclo dentro da janela do teste.
    /// </summary>
    private static async Task RunOneCycleAsync(Atua.Channel.Worker worker)
    {
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await worker.StopAsync(CancellationToken.None);
    }
}
