using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de <see cref="TriagemOutboxDrainService"/> (ADR-041) usando
/// <see cref="FakeTriagemOutboxRepository"/> (simula o claim sem PostgreSQL
/// real) + <see cref="StartTriagemUseCase"/> real, com os fakes já
/// existentes de <see cref="FakeTriagemRepository"/>/<see cref="FakeTriagemEligibleWorkOrderQuery"/>.
/// </summary>
public class TriagemOutboxDrainServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid IntegrationId = Guid.NewGuid();

    [Fact]
    public async Task CreatedOuExisting_MarcaProcessed()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(TenantId, IntegrationId, "OS-1", workOrderId, "assigned");

        var (service, outboxRepository) = CreateService(workOrderQuery, now);
        var item = NewOutboxItem("OS-1", now);
        outboxRepository.Add(item);

        var result = await service.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Retried);
        Assert.Equal(0, result.Failed);
        Assert.Equal(ETriagemTriggerOutboxStatus.Processed, item.Status);
        Assert.Equal(1, outboxRepository.SaveChangesCallCount);
    }

    [Theory]
    [InlineData("não encontrada")]
    [InlineData("não elegível")]
    public async Task NotFoundOuNotEligible_MarcaSkippedSemRetry(string cenario)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        if (cenario == "não elegível")
        {
            workOrderQuery.Seed(TenantId, IntegrationId, "OS-1", Guid.NewGuid(), "pending");
        }
        // cenário "não encontrada": nada semeado — ExecuteAsync retorna null.

        var (service, outboxRepository) = CreateService(workOrderQuery, now);
        var item = NewOutboxItem("OS-1", now);
        outboxRepository.Add(item);

        var result = await service.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Retried);
        Assert.Equal(0, result.Failed);
        Assert.Equal(ETriagemTriggerOutboxStatus.Skipped, item.Status);
        Assert.Equal(0, item.AttemptCount);
        Assert.Equal(
            cenario == "não elegível" ? ETriagemOutboxErrorCode.WorkOrderNotEligible : ETriagemOutboxErrorCode.WorkOrderNotFound,
            item.LastErrorCode);
    }

    [Fact]
    public async Task ExcecaoNaoClassificada_RetentaComBackoff()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var workOrderQuery = new ThrowingTriagemEligibleWorkOrderQuery();

        var (service, outboxRepository) = CreateService(workOrderQuery, now, maxAttempts: 10, baseRetrySeconds: 5,
            maxRetrySeconds: 300);
        var item = NewOutboxItem("OS-1", now);
        outboxRepository.Add(item);

        var result = await service.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.Retried);
        Assert.Equal(0, result.Failed);
        Assert.Equal(ETriagemTriggerOutboxStatus.Pending, item.Status);
        Assert.Equal(1, item.AttemptCount);
        Assert.Equal(ETriagemOutboxErrorCode.Unclassified, item.LastErrorCode);
        Assert.Equal(now.AddSeconds(5), item.NextAttemptAtUtc);
        Assert.Null(item.ClaimedAtUtc);
        Assert.Null(item.ClaimExpiresAtUtc);
    }

    [Fact]
    public async Task AtingirMaxAttempts_MarcaFailed()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var workOrderQuery = new ThrowingTriagemEligibleWorkOrderQuery();

        var (service, outboxRepository) = CreateService(workOrderQuery, now, maxAttempts: 1);
        var item = NewOutboxItem("OS-1", now);
        outboxRepository.Add(item);

        var result = await service.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(0, result.Retried);
        Assert.Equal(1, result.Failed);
        Assert.Equal(ETriagemTriggerOutboxStatus.Failed, item.Status);
        Assert.Equal(1, item.AttemptCount);
    }

    [Fact]
    public async Task CancelamentoAntesDeProcessarOItem_NaoMarcaFalhaEDeixaProcessing()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        workOrderQuery.Seed(TenantId, IntegrationId, "OS-1", Guid.NewGuid(), "assigned");

        var (service, outboxRepository) = CreateService(workOrderQuery, now);
        var item = NewOutboxItem("OS-1", now);
        outboxRepository.Add(item);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.DrainOnceAsync(cts.Token);

        Assert.Equal(0, result.Processed);
        Assert.Equal(0, result.Failed);
        // Claim já ocorreu (parte do próprio teste), então o item segue
        // Processing — nunca marcado como falha por cancelamento (ADR-041).
        Assert.Equal(ETriagemTriggerOutboxStatus.Processing, item.Status);
    }

    [Fact]
    public async Task LeaseExpirada_TornaItemElegivelNovamenteParaClaim()
    {
        var claimedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var afterLeaseExpiry = claimedAt.AddSeconds(120); // lease de 60s já expirada
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(TenantId, IntegrationId, "OS-1", workOrderId, "assigned");

        var outboxRepository = new FakeTriagemOutboxRepository();
        var item = NewOutboxItem("OS-1", claimedAt);
        outboxRepository.Add(item);

        // Primeiro claim deixa o item Processing (lease de 60s).
        await outboxRepository.ClaimBatchAsync(20, TimeSpan.FromSeconds(60), claimedAt, CancellationToken.None);
        Assert.Equal(ETriagemTriggerOutboxStatus.Processing, item.Status);

        // Reivindicação após a lease expirar deve devolver o mesmo item de novo.
        var reclaimed = await outboxRepository.ClaimBatchAsync(20, TimeSpan.FromSeconds(60), afterLeaseExpiry,
            CancellationToken.None);

        Assert.Single(reclaimed);
        Assert.Same(item, reclaimed[0]);
    }

    [Fact]
    public async Task RespeitaOTamanhoDoLote()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();

        var (service, outboxRepository) = CreateService(workOrderQuery, now, batchSize: 2);
        for (var i = 0; i < 5; i++)
        {
            var providerId = $"OS-{i}";
            workOrderQuery.Seed(TenantId, IntegrationId, providerId, Guid.NewGuid(), "assigned");
            outboxRepository.Add(NewOutboxItem(providerId, now));
        }

        var result = await service.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(2, result.BatchSize);
        Assert.Equal(2, result.Processed);
    }

    [Fact]
    public void NaoDependeDeICollectorApiClientOuDeChamadaHttp()
    {
        // TriagemOutboxDrainService só depende de ITriagemOutboxRepository,
        // StartTriagemUseCase, IOptions<TriagemOutboxOptions>, TimeProvider e
        // ILogger — nenhum cliente HTTP/ICollectorApiClient (ADR-041: nunca
        // chama a rota manual POST /api/internal/triagens/start).
        var constructorParameterTypes = typeof(TriagemOutboxDrainService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.FullName)
            .ToList();

        Assert.DoesNotContain(constructorParameterTypes, type => type != null &&
            (type.Contains("CollectorApiClient") || type.Contains("HttpClient")));
    }

    [Fact]
    public async Task CanalRetornaFalhaTransitoria_OutboxClassificaComoRetentavelComBackoff()
    {
        // ADR-041: a exceção tipada do canal (TriagemChannelTransientFailureException)
        // deve ser classificada como erro transitório elegível a retentativa
        // com backoff — nunca marcada como falha definitiva nem mascarada
        // como "Unclassified".
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(TenantId, IntegrationId, "OS-1", workOrderId, "assigned");
        var channel = new FakeTriagemChannel();
        var idempotencyKey = new TriagemIdempotencyKey(TenantId, IntegrationId, "OS-1").ToString();
        channel.ConfigureResult(idempotencyKey,
            TriagemChannelSendResult.TransientFailure("timeout-provedor", "Tempo esgotado."));

        var (service, outboxRepository) = CreateService(workOrderQuery, now, maxAttempts: 10, baseRetrySeconds: 5,
            maxRetrySeconds: 300, channel: channel);
        var item = NewOutboxItem("OS-1", now);
        outboxRepository.Add(item);

        var result = await service.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.Retried);
        Assert.Equal(0, result.Failed);
        Assert.Equal(ETriagemTriggerOutboxStatus.Pending, item.Status);
        Assert.Equal(1, item.AttemptCount);
        Assert.Equal(ETriagemOutboxErrorCode.TransientError, item.LastErrorCode);
        Assert.Equal(now.AddSeconds(5), item.NextAttemptAtUtc);
        Assert.Equal(1, channel.RealSendCount);
    }

    private static TriagemTriggerOutbox NewOutboxItem(string workOrderProviderId, DateTimeOffset occurredAtUtc) =>
        new(Guid.NewGuid(), TenantId, IntegrationId, Guid.NewGuid(), workOrderProviderId, occurredAtUtc, occurredAtUtc);

    private static (TriagemOutboxDrainService Service, FakeTriagemOutboxRepository Repository) CreateService(
        Atua.Api.Application.Triagens.Contracts.ITriagemEligibleWorkOrderQuery workOrderQuery,
        DateTimeOffset now,
        int batchSize = 20,
        int maxAttempts = 10,
        int baseRetrySeconds = 5,
        int maxRetrySeconds = 300,
        ITriagemChannel? channel = null)
    {
        var outboxRepository = new FakeTriagemOutboxRepository();
        var triagemRepository = new FakeTriagemRepository();
        var useCase = new StartTriagemUseCase(workOrderQuery, triagemRepository, channel ?? new FakeTriagemChannel(),
            new FakeTimeProvider(now));
        var options = Options.Create(new TriagemOutboxOptions
        {
            BatchSize = batchSize,
            LeaseSeconds = 60,
            MaxAttempts = maxAttempts,
            BaseRetrySeconds = baseRetrySeconds,
            MaxRetrySeconds = maxRetrySeconds,
        });

        var service = new TriagemOutboxDrainService(
            outboxRepository, useCase, options, new FakeTimeProvider(now), NullLogger<TriagemOutboxDrainService>.Instance);

        return (service, outboxRepository);
    }
}

/// <summary>
/// Fake que sempre lança — simula uma falha transitória/não classificada
/// durante a revalidação da OS, para exercitar o caminho de retry/backoff de
/// <see cref="TriagemOutboxDrainService"/>.
/// </summary>
internal sealed class ThrowingTriagemEligibleWorkOrderQuery : Atua.Api.Application.Triagens.Contracts.ITriagemEligibleWorkOrderQuery
{
    public Task<Atua.Api.Application.Triagens.Contracts.TriagemEligibleWorkOrderLookupResult?> ExecuteAsync(
        Guid tenantId, Guid integrationId, string workOrderProviderId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Falha simulada de infraestrutura.");
}
