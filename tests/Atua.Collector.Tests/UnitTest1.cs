using Atua.Collector.Api;
using Atua.Collector.Configuration;
using Atua.Collector.Contracts;
using Atua.Collector.IService;
using Atua.Collector.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Atua.Collector.Tests;

/// <summary>
/// Testes unitários do Worker — foco no novo fluxo de elegibilidade (RF-009.2/RN-009.2).
/// BUG-009-001 fix: CheckEligibilityAsync deve ser chamado ANTES de CollectAsync.
/// </summary>
public class WorkerEligibilityTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ICollectorApiClient ApiClient, IIServiceCollector IService, IWorkOrderRepository Repo, Worker Worker)
        BuildWorker()
    {
        var apiClient = Substitute.For<ICollectorApiClient>();
        var iService = Substitute.For<IIServiceCollector>();
        var repo = Substitute.For<IWorkOrderRepository>();
        var options = Options.Create(new CollectorWorkerOptions { PollingIntervalSeconds = 1 });
        var logger = NullLogger<Worker>.Instance;
        var worker = new Worker(apiClient, iService, repo, options, logger);
        return (apiClient, iService, repo, worker);
    }

    private static ClaimResponse MakeCommand(Guid? commandId = null, Guid? tenantId = null) =>
        new(
            commandId ?? Guid.CreateVersion7(),
            "ImmediateCollection",
            Guid.CreateVersion7(),
            tenantId ?? Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(5),
            3,
            new CredentialPayload("user@test.com", "s3cr3t", "https://iservice.test"));

    private static CollectionResult MakeResult() =>
        new(DateTimeOffset.UtcNow,
            new Dictionary<string, int>(),
            new Dictionary<string, IReadOnlyList<object>>());

    // -----------------------------------------------------------------------
    // Cenário 1 — Elegibilidade negada: CollectAsync NÃO deve ser chamado
    //             e CompleteAsync deve ser chamado com "Cancelled"
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QuandoElegibilidadeNegada_NaoChamaCollect_ECompletaComCancelled()
    {
        var (apiClient, iService, repo, worker) = BuildWorker();
        var commandId = Guid.CreateVersion7();
        var command = MakeCommand(commandId: commandId);

        apiClient.ClaimAsync(Arg.Any<CancellationToken>()).Returns(command);
        apiClient.CheckEligibilityAsync(Arg.Any<CancellationToken>())
            .Returns(new EligibilityResponse(Eligible: false, DateTimeOffset.UtcNow));
        apiClient.CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ECommandFailureReason?>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteResponse(commandId, "Cancelled", DateTimeOffset.UtcNow));

        // Executa um único ciclo via CancellationToken que cancela logo após o claim
        using var cts = new CancellationTokenSource();
        // Após o primeiro ciclo completo, fazemos ClaimAsync retornar null para parar o loop
        var callCount = 0;
        apiClient.ClaimAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount == 1) return Task.FromResult<ClaimResponse?>(command);
            cts.Cancel();
            return Task.FromResult<ClaimResponse?>(null);
        });

        await worker.StartAsync(CancellationToken.None);
        try { await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* expected */ }
        await worker.StopAsync(CancellationToken.None);

        // CollectAsync NUNCA deve ter sido chamado
        await iService.DidNotReceive().CollectAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());

        // Nenhuma persistência no Mongo
        await repo.DidNotReceive().UpsertSnapshotsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CollectionResult>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().InsertObservationsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CollectionResult>(), Arg.Any<CancellationToken>());

        // CompleteAsync deve ter sido chamado com "Cancelled"
        await apiClient.Received(1).CompleteAsync(
            commandId,
            "Cancelled",
            null,
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Cenário 2 — Elegibilidade concedida: CollectAsync DEVE ser chamado
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QuandoElegibilidadeConcedida_ChamaCollect_ECompletaComSucceeded()
    {
        var (apiClient, iService, repo, worker) = BuildWorker();
        var commandId = Guid.CreateVersion7();
        var command = MakeCommand(commandId: commandId);
        var result = MakeResult();

        var callCount = 0;
        using var cts = new CancellationTokenSource();
        apiClient.ClaimAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount == 1) return Task.FromResult<ClaimResponse?>(command);
            cts.Cancel();
            return Task.FromResult<ClaimResponse?>(null);
        });
        apiClient.CheckEligibilityAsync(Arg.Any<CancellationToken>())
            .Returns(new EligibilityResponse(Eligible: true, DateTimeOffset.UtcNow));
        iService.CollectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(result);
        apiClient.CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ECommandFailureReason?>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteResponse(commandId, "Succeeded", DateTimeOffset.UtcNow));

        await worker.StartAsync(CancellationToken.None);
        try { await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* expected */ }
        await worker.StopAsync(CancellationToken.None);

        // CollectAsync DEVE ter sido chamado exatamente uma vez
        await iService.Received(1).CollectAsync(
            command.Credential.Username,
            command.Credential.Password,
            command.Credential.BaseUrl,
            command.HistoryWindowMonths,
            Arg.Any<CancellationToken>());

        // CompleteAsync deve ter sido chamado com "Succeeded"
        await apiClient.Received(1).CompleteAsync(
            commandId,
            "Succeeded",
            null,
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Cenário 3 — Falha na chamada de elegibilidade: deve completar como Failed/IServiceUnavailable
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QuandoCheckEligibilityFalha_NaoChamaCollect_ECompletaComFailed()
    {
        var (apiClient, iService, repo, worker) = BuildWorker();
        var commandId = Guid.CreateVersion7();
        var command = MakeCommand(commandId: commandId);

        var callCount = 0;
        using var cts = new CancellationTokenSource();
        apiClient.ClaimAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount == 1) return Task.FromResult<ClaimResponse?>(command);
            cts.Cancel();
            return Task.FromResult<ClaimResponse?>(null);
        });
        apiClient.CheckEligibilityAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
        apiClient.CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ECommandFailureReason?>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteResponse(commandId, "Failed", DateTimeOffset.UtcNow));

        await worker.StartAsync(CancellationToken.None);
        try { await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* expected */ }
        await worker.StopAsync(CancellationToken.None);

        // CollectAsync nunca deve ser chamado
        await iService.DidNotReceive().CollectAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());

        // CompleteAsync deve ter sido chamado com "Failed" e IServiceUnavailable
        await apiClient.Received(1).CompleteAsync(
            commandId,
            "Failed",
            ECommandFailureReason.IServiceUnavailable,
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Cenário 4 — Sem comando (204): CheckEligibilityAsync NÃO deve ser chamado
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QuandoSemComando_NaoVerificaElegibilidade()
    {
        var (apiClient, iService, repo, worker) = BuildWorker();

        var callCount = 0;
        using var cts = new CancellationTokenSource();
        apiClient.ClaimAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount >= 1) cts.Cancel();
            return Task.FromResult<ClaimResponse?>(null);
        });

        await worker.StartAsync(CancellationToken.None);
        try { await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* expected */ }
        await worker.StopAsync(CancellationToken.None);

        await apiClient.DidNotReceive().CheckEligibilityAsync(Arg.Any<CancellationToken>());
        await iService.DidNotReceive().CollectAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
