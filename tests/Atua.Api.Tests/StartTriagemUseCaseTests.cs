using Atua.Api.Application.Triagens;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Testes do caso de uso idempotente de início da triagem (Incremento 2,
/// ADR-039, arquitetura seção 6). Usa <see cref="FakeTriagemRepository"/>
/// (ConcurrentDictionary, sem atrasos artificiais) no lugar de um
/// armazenamento físico, que fica para incremento futuro.
/// </summary>
public class StartTriagemUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static StartTriagemUseCase CreateUseCase(FakeTriagemRepository repository, DateTimeOffset? now = null) =>
        new(repository, new FakeTimeProvider(now ?? Now));

    [Fact]
    public async Task PrimeiraExecucao_CriaTriagemAguardandoEnvio()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "OS-1",
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, result.Status);
        Assert.NotEqual(Guid.Empty, result.TriagemId);
    }

    [Fact]
    public async Task RepeticaoSequencialComMesmaChave_RetornaMesmoIdECreatedFalse()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        var first = await useCase.ExecuteAsync(tenantId, integrationId, workOrderId, "OS-1", CancellationToken.None);
        var second = await useCase.ExecuteAsync(tenantId, integrationId, workOrderId, "OS-1", CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.TriagemId, second.TriagemId);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, second.Status);
    }

    [Fact]
    public async Task ChamadasConcorrentesComMesmaChave_ProduzemExatamenteUmCreatedEUmUnicoId()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        const int concurrency = 50;

        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => useCase.ExecuteAsync(tenantId, integrationId, workOrderId, "OS-concorrente",
                CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(result => result.Created));
        Assert.Single(results.Select(result => result.TriagemId).Distinct());
    }

    [Fact]
    public async Task ChavesDistintasPorTenant_CriamTriagensDistintas()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        var first = await useCase.ExecuteAsync(Guid.NewGuid(), integrationId, workOrderId, "OS-1",
            CancellationToken.None);
        var second = await useCase.ExecuteAsync(Guid.NewGuid(), integrationId, workOrderId, "OS-1",
            CancellationToken.None);

        Assert.True(first.Created);
        Assert.True(second.Created);
        Assert.NotEqual(first.TriagemId, second.TriagemId);
    }

    [Fact]
    public async Task ChavesDistintasPorIntegracao_CriamTriagensDistintas()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);
        var tenantId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        var first = await useCase.ExecuteAsync(tenantId, Guid.NewGuid(), workOrderId, "OS-1",
            CancellationToken.None);
        var second = await useCase.ExecuteAsync(tenantId, Guid.NewGuid(), workOrderId, "OS-1",
            CancellationToken.None);

        Assert.True(first.Created);
        Assert.True(second.Created);
        Assert.NotEqual(first.TriagemId, second.TriagemId);
    }

    [Fact]
    public async Task ChavesDistintasPorOsExterna_CriamTriagensDistintas()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();

        var first = await useCase.ExecuteAsync(tenantId, integrationId, Guid.NewGuid(), "OS-1",
            CancellationToken.None);
        var second = await useCase.ExecuteAsync(tenantId, integrationId, Guid.NewGuid(), "OS-2",
            CancellationToken.None);

        Assert.True(first.Created);
        Assert.True(second.Created);
        Assert.NotEqual(first.TriagemId, second.TriagemId);
    }

    [Fact]
    public async Task ExistenteComEstadoAvancado_EhRetornadaSemResetDeEstado()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        var first = await useCase.ExecuteAsync(tenantId, integrationId, workOrderId, "OS-1", CancellationToken.None);

        var stored = (await repository.GetOrAddAsync(
            new TriagemIdempotencyKey(tenantId, integrationId, "OS-1"),
            new Triagem(Guid.NewGuid(), tenantId, integrationId, workOrderId, Now),
            CancellationToken.None)).Triagem;
        stored.MarcarContatoIniciado(Now);
        stored.IniciarColeta(Now);

        var second = await useCase.ExecuteAsync(tenantId, integrationId, workOrderId, "OS-1", CancellationToken.None);

        Assert.False(second.Created);
        Assert.Equal(first.TriagemId, second.TriagemId);
        Assert.Equal(ETriagemStatus.EmColeta, second.Status);
    }

    [Fact]
    public async Task TimeProviderControlaCreatedAtUtcEUpdatedAtUtc()
    {
        var repository = new FakeTriagemRepository();
        var fixedNow = new DateTimeOffset(2026, 10, 13, 15, 30, 0, TimeSpan.Zero);
        var useCase = CreateUseCase(repository, fixedNow);
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        var result = await useCase.ExecuteAsync(tenantId, integrationId, workOrderId, "OS-1",
            CancellationToken.None);

        // Busca a instância persistida (mesma chave) para inspecionar os
        // timestamps controlados pelo TimeProvider injetado no caso de uso.
        var stored = (await repository.GetOrAddAsync(
            new TriagemIdempotencyKey(tenantId, integrationId, "OS-1"),
            candidate: new Triagem(Guid.NewGuid(), tenantId, integrationId, workOrderId, DateTimeOffset.UtcNow),
            CancellationToken.None)).Triagem;

        Assert.Equal(result.TriagemId, stored.Id);
        Assert.Equal(fixedNow, stored.CreatedAtUtc);
        Assert.Equal(fixedNow, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task WorkOrderIdVazio_FalhaExplicitamente()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "OS-1", CancellationToken.None));
    }

    [Fact]
    public async Task TenantIdVazio_FalhaExplicitamente()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "OS-1", CancellationToken.None));
    }

    [Fact]
    public async Task IntegrationIdVazio_FalhaExplicitamente()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "OS-1", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task WorkOrderProviderIdInvalido_FalhaExplicitamente(string? workOrderProviderId)
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), workOrderProviderId!,
                CancellationToken.None));
    }

    [Fact]
    public async Task CancellationTokenCancelado_NaoProduzRegistro()
    {
        var repository = new FakeTriagemRepository();
        var useCase = CreateUseCase(repository);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(tenantId, integrationId, workOrderId, "OS-1", cts.Token));

        // Nenhum registro deve existir para a chave após o cancelamento: uma
        // execução subsequente sem cancelamento deve criar normalmente.
        var afterCancellation = await useCase.ExecuteAsync(tenantId, integrationId, workOrderId, "OS-1",
            CancellationToken.None);
        Assert.True(afterCancellation.Created);
    }
}
