using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Testes do caso de uso idempotente de início da triagem (ADR-039,
/// arquitetura seção 3, itens 2/3, 5.3). O WorkOrderId interno nunca é
/// recebido do chamador: é sempre resolvido por
/// <see cref="FakeTriagemEligibleWorkOrderQuery"/> (tenant + integração +
/// WorkOrderProviderId), que também determina elegibilidade.
/// <see cref="FakeTriagemRepository"/> substitui um armazenamento físico
/// (clona a cada leitura e arbitra concorrência por token, como o
/// PostgreSQL real). <see cref="FakeTriagemChannel"/> substitui o canal
/// (dedup por chave de idempotência, como exigido pelo ADR-039). Desde que
/// <c>StartTriagemUseCase</c> emite o <c>SendTemplate</c> e persiste o
/// resultado, uma criação bem-sucedida avança sincronamente de
/// <see cref="ETriagemStatus.AguardandoEnvio"/> para
/// <see cref="ETriagemStatus.ContatoIniciado"/> dentro da mesma chamada.
/// </summary>
public class StartTriagemUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static StartTriagemUseCase CreateUseCase(FakeTriagemRepository repository,
        FakeTriagemEligibleWorkOrderQuery workOrderQuery, DateTimeOffset? now = null,
        ITriagemChannel? channel = null) =>
        new(workOrderQuery, repository, channel ?? new FakeTriagemChannel(), new FakeTimeProvider(now ?? Now));

    [Fact]
    public async Task OsElegivelAssigned_PrimeiraExecucao_CriaTriagemEEnviaTemplateComSucesso()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var channel = new FakeTriagemChannel();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery, channel: channel);

        var result = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, result.Outcome);
        // O envio padrão do FakeTriagemChannel é sucesso — a triagem já
        // avança para ContatoIniciado dentro da mesma chamada que a criou
        // (arquitetura, seção 5.3: a Master API decide quando emitir
        // SendTemplate; aqui ela o faz imediatamente após aceitar o
        // gatilho).
        Assert.Equal(ETriagemStatus.ContatoIniciado, result.Status);
        Assert.NotNull(result.TriagemId);
        Assert.NotEqual(Guid.Empty, result.TriagemId!.Value);
        Assert.Equal(1, channel.RealSendCount);
    }

    [Fact]
    public async Task MesmaOsElegivel_RepeticaoSequencial_RetornaExistingSemReenviarAoCanal()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var channel = new FakeTriagemChannel();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery, channel: channel);

        var first = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);
        var second = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, first.Outcome);
        Assert.Equal(EStartTriagemOutcome.Existing, second.Outcome);
        Assert.Equal(first.TriagemId, second.TriagemId);
        // Estado já avançado (ContatoIniciado) além de AguardandoEnvio —
        // curto-circuito: a reentrega nunca reenvia o template.
        Assert.Equal(ETriagemStatus.ContatoIniciado, second.Status);
        Assert.Equal(1, channel.RealSendCount);
    }

    [Fact]
    public async Task ChamadasConcorrentesComMesmaChaveElegivel_ProduzemExatamenteUmCreated()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var channel = new FakeTriagemChannel();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-concorrente", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery, channel: channel);
        const int concurrency = 50;

        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => useCase.ExecuteAsync(tenantId, integrationId, "OS-concorrente", CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(result => result.Outcome == EStartTriagemOutcome.Created));
        Assert.Equal(concurrency, results.Count(result => result.Outcome is EStartTriagemOutcome.Created
            or EStartTriagemOutcome.Existing));
        Assert.Single(results.Select(result => result.TriagemId).Distinct());
        // Todas as 50 chamadas convergem para o mesmo estado final e o canal
        // fake — que deduplica por chave de idempotência mesmo sob
        // concorrência real (Task.WhenAll) — só executa o envio de fato uma
        // única vez, independentemente de qual execução "ganhou" a
        // persistência otimista.
        Assert.All(results, result => Assert.Equal(ETriagemStatus.ContatoIniciado, result.Status));
        Assert.Equal(1, channel.RealSendCount);
    }

    [Fact]
    public async Task OsInexistente_RetornaWorkOrderNotFoundSemCriarTriagem()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var useCase = CreateUseCase(repository, workOrderQuery);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), "OS-inexistente",
            CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.WorkOrderNotFound, result.Outcome);
        Assert.Null(result.TriagemId);
        Assert.Null(result.Status);
    }

    [Fact]
    public async Task OsDeOutroTenant_NaoEhAlcancada_RetornaWorkOrderNotFound()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(Guid.NewGuid(), integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);

        // Tenant diferente do que foi usado para popular a OS — mesma
        // integração e mesmo WorkOrderProviderId, ainda assim não alcançável.
        var result = await useCase.ExecuteAsync(Guid.NewGuid(), integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.WorkOrderNotFound, result.Outcome);
    }

    [Fact]
    public async Task OsDeOutraIntegracao_NaoEhAlcancada_RetornaWorkOrderNotFound()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, Guid.NewGuid(), "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);

        // Mesmo tenant e mesmo WorkOrderProviderId, integração diferente da
        // que foi usada para popular a OS.
        var result = await useCase.ExecuteAsync(tenantId, Guid.NewGuid(), "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.WorkOrderNotFound, result.Outcome);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("accepted")]
    [InlineData("closed")]
    [InlineData("Assigned")]
    [InlineData("ASSIGNED")]
    public async Task OsComStatusDiferenteDeAssigned_RetornaWorkOrderNotEligibleSemCriarTriagem(string status)
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, status);
        var useCase = CreateUseCase(repository, workOrderQuery);

        var result = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.WorkOrderNotEligible, result.Outcome);
        Assert.Null(result.TriagemId);
        Assert.Null(result.Status);

        // Nenhuma triagem deve ter sido registrada no repositório.
        var stored = await repository.GetOrAddAsync(
            new TriagemIdempotencyKey(tenantId, integrationId, "OS-1"),
            new Triagem(Guid.NewGuid(), tenantId, integrationId, workOrderId, Now),
            CancellationToken.None);
        Assert.True(stored.Created);
    }

    [Fact]
    public async Task ChavesDistintasPorTenant_CriamTriagensDistintas()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var integrationId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        workOrderQuery.Seed(tenantA, integrationId, "OS-1", Guid.NewGuid(), "assigned");
        workOrderQuery.Seed(tenantB, integrationId, "OS-1", Guid.NewGuid(), "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);

        var first = await useCase.ExecuteAsync(tenantA, integrationId, "OS-1", CancellationToken.None);
        var second = await useCase.ExecuteAsync(tenantB, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, first.Outcome);
        Assert.Equal(EStartTriagemOutcome.Created, second.Outcome);
        Assert.NotEqual(first.TriagemId, second.TriagemId);
    }

    [Fact]
    public async Task ChavesDistintasPorIntegracao_CriamTriagensDistintas()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationA = Guid.NewGuid();
        var integrationB = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationA, "OS-1", Guid.NewGuid(), "assigned");
        workOrderQuery.Seed(tenantId, integrationB, "OS-1", Guid.NewGuid(), "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);

        var first = await useCase.ExecuteAsync(tenantId, integrationA, "OS-1", CancellationToken.None);
        var second = await useCase.ExecuteAsync(tenantId, integrationB, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, first.Outcome);
        Assert.Equal(EStartTriagemOutcome.Created, second.Outcome);
        Assert.NotEqual(first.TriagemId, second.TriagemId);
    }

    [Fact]
    public async Task ChavesDistintasPorOsExterna_CriamTriagensDistintas()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", Guid.NewGuid(), "assigned");
        workOrderQuery.Seed(tenantId, integrationId, "OS-2", Guid.NewGuid(), "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);

        var first = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);
        var second = await useCase.ExecuteAsync(tenantId, integrationId, "OS-2", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, first.Outcome);
        Assert.Equal(EStartTriagemOutcome.Created, second.Outcome);
        Assert.NotEqual(first.TriagemId, second.TriagemId);
    }

    [Fact]
    public async Task ExistenteComEstadoAvancado_EhRetornadaSemResetDeEstado()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);

        var first = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        // A primeira execução já envia o template com sucesso (fake padrão)
        // e persiste a transição para ContatoIniciado — avançamos
        // manualmente mais um estado (EmColeta) para comprovar que o caso de
        // uso nunca "reseta" um estado já avançado além de AguardandoEnvio.
        var key = new TriagemIdempotencyKey(tenantId, integrationId, "OS-1");
        var stored = await repository.FindByKeyAsync(key, CancellationToken.None);
        Assert.NotNull(stored);
        stored!.IniciarColeta(Now);
        await repository.SaveTransitionAsync(stored, CancellationToken.None);

        var second = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Existing, second.Outcome);
        Assert.Equal(first.TriagemId, second.TriagemId);
        Assert.Equal(ETriagemStatus.EmColeta, second.Status);
    }

    [Fact]
    public async Task TimeProviderControlaCreatedAtUtcEUpdatedAtUtc()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var fixedNow = new DateTimeOffset(2026, 10, 13, 15, 30, 0, TimeSpan.Zero);
        var useCase = CreateUseCase(repository, workOrderQuery, fixedNow);

        var result = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

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
    public async Task TenantIdVazio_FalhaExplicitamente()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var useCase = CreateUseCase(repository, workOrderQuery);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(Guid.Empty, Guid.NewGuid(), "OS-1", CancellationToken.None));
    }

    [Fact]
    public async Task IntegrationIdVazio_FalhaExplicitamente()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var useCase = CreateUseCase(repository, workOrderQuery);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(Guid.NewGuid(), Guid.Empty, "OS-1", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task WorkOrderProviderIdInvalido_FalhaExplicitamente(string? workOrderProviderId)
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var useCase = CreateUseCase(repository, workOrderQuery);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), workOrderProviderId!, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationTokenCancelado_NaoProduzRegistro()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(tenantId, integrationId, "OS-1", cts.Token));

        // Nenhum registro deve existir para a chave após o cancelamento: uma
        // execução subsequente sem cancelamento deve criar normalmente.
        var afterCancellation = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);
        Assert.Equal(EStartTriagemOutcome.Created, afterCancellation.Outcome);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("pending")]
    public async Task TriagemExistente_RetornaExistingMesmoComWorkOrderNaoMaisAssigned(string statusAtual)
    {
        // ADR-039: reentrega da mesma chave retorna o resultado conhecido.
        // A elegibilidade `assigned` é exigida apenas antes de CRIAR — uma
        // OS que avançou para outro status após a triagem já ter sido
        // registrada não deve impedir a reentrega de retornar a triagem
        // existente.
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);
        var first = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        // A OS avança para um status não elegível depois que a triagem já
        // foi registrada.
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, statusAtual);

        var second = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, first.Outcome);
        Assert.Equal(EStartTriagemOutcome.Existing, second.Outcome);
        Assert.Equal(first.TriagemId, second.TriagemId);
        // A primeira execução já envia o template (fake padrão = sucesso) e
        // avança para ContatoIniciado; a reentrega apenas devolve o estado
        // já alcançado, sem reconsultar a OS nem reenviar.
        Assert.Equal(ETriagemStatus.ContatoIniciado, second.Status);
    }

    [Fact]
    public async Task TriagemExistente_RetornaExistingSemConsultarWorkOrderNovamente()
    {
        // Prova que a consulta de OS nem sequer é acionada quando já existe
        // triagem registrada para a chave — não apenas que seu resultado é
        // ignorado.
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);
        var first = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);
        var callsAfterFirst = workOrderQuery.CallCount;

        // A OS deixa de ser encontrada pela consulta — simula remoção ou
        // indisponibilidade momentânea do provedor.
        workOrderQuery.Remove(tenantId, integrationId, "OS-1");

        var second = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, first.Outcome);
        Assert.Equal(EStartTriagemOutcome.Existing, second.Outcome);
        Assert.Equal(first.TriagemId, second.TriagemId);
        Assert.Equal(callsAfterFirst, workOrderQuery.CallCount);
    }

    [Fact]
    public async Task CanalRetornaFalhaPermanente_PersisteMarcarFalhaDeContato()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var channel = new FakeTriagemChannel();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var key = new TriagemIdempotencyKey(tenantId, integrationId, "OS-1");
        channel.ConfigureResult(key.ToString(),
            TriagemChannelSendResult.PermanentFailure("template-rejeitado", "Número de contato inválido."));
        var useCase = CreateUseCase(repository, workOrderQuery, channel: channel);

        var result = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, result.Outcome);
        Assert.Equal(ETriagemStatus.FalhaDeContato, result.Status);

        var stored = await repository.FindByKeyAsync(key, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(ETriagemStatus.FalhaDeContato, stored!.Status);
        Assert.Equal(1, channel.RealSendCount);
    }

    [Fact]
    public async Task CanalRetornaFalhaTransitoria_MantemAguardandoEnvioELancaExcecaoTipada()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var channel = new FakeTriagemChannel();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var key = new TriagemIdempotencyKey(tenantId, integrationId, "OS-1");
        channel.ConfigureResult(key.ToString(),
            TriagemChannelSendResult.TransientFailure("timeout-provedor", "Tempo esgotado ao contatar o provedor."));
        var useCase = CreateUseCase(repository, workOrderQuery, channel: channel);

        var exception = await Assert.ThrowsAsync<TriagemChannelTransientFailureException>(() =>
            useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None));
        Assert.Equal("timeout-provedor", exception.ChannelErrorCode);

        // Nenhuma transição foi persistida: a triagem permanece
        // AguardandoEnvio, preservando a mesma chave de idempotência para
        // que a outbox (ADR-041) possa classificar esta exceção e reagendar
        // retentativa.
        var stored = await repository.FindByKeyAsync(key, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, stored!.Status);
    }

    [Fact]
    public async Task FalhaTransitoriaSeguidaDeRetomada_ReenviaComMesmaChaveEConcluiComSucesso()
    {
        // "Replay resolvido": a primeira tentativa falha transitoriamente
        // (ex.: timeout do provedor); uma reentrega subsequente do mesmo
        // gatilho (mesma chave) retoma o envio — a triagem segue
        // AguardandoEnvio entre as duas tentativas — e conclui com sucesso.
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var channel = new FakeTriagemChannel();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var key = new TriagemIdempotencyKey(tenantId, integrationId, "OS-1");
        channel.ConfigureResult(key.ToString(),
            TriagemChannelSendResult.TransientFailure("timeout-provedor", "Tempo esgotado."));
        var useCase = CreateUseCase(repository, workOrderQuery, channel: channel);

        await Assert.ThrowsAsync<TriagemChannelTransientFailureException>(() =>
            useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None));

        // O mesmo canal singleton passa a responder com sucesso na
        // retentativa. Falha transitória não pode ficar memorizada, pois o
        // worker reutiliza a mesma instância enquanto reagenda a outbox.
        channel.ConfigureResult(key.ToString(), TriagemChannelSendResult.Success("fake-retry-success"));

        var retried = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Existing, retried.Outcome);
        Assert.Equal(ETriagemStatus.ContatoIniciado, retried.Status);
        Assert.Equal(2, channel.RealSendCount);

        var stored = await repository.FindByKeyAsync(key, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(ETriagemStatus.ContatoIniciado, stored!.Status);
    }

    [Fact]
    public async Task CrashRetryComNovaInstanciaDeCanal_NaoReenviaQuandoJaContatoIniciado()
    {
        // Simula reentrega após "crash" do worker da outbox (ADR-041): uma
        // nova instância do canal (processo reiniciado, sem cache de
        // deduplicação em memória do incremento anterior) nunca é chamada
        // porque a triagem já persistiu ContatoIniciado antes do reinício —
        // a deduplicação real vem do ESTADO PERSISTIDO, não do cache do
        // canal.
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var firstChannel = new FakeTriagemChannel();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery, channel: firstChannel);

        var first = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);
        Assert.Equal(ETriagemStatus.ContatoIniciado, first.Status);
        Assert.Equal(1, firstChannel.RealSendCount);

        // "Processo reiniciado": nova instância do canal, sem qualquer
        // estado de deduplicação compartilhado com a anterior.
        var afterCrashChannel = new FakeTriagemChannel();
        var afterCrashUseCase = CreateUseCase(repository, workOrderQuery, channel: afterCrashChannel);

        var second = await afterCrashUseCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Existing, second.Outcome);
        Assert.Equal(first.TriagemId, second.TriagemId);
        Assert.Equal(ETriagemStatus.ContatoIniciado, second.Status);
        Assert.Equal(0, afterCrashChannel.RealSendCount);
    }

    [Fact]
    public async Task ConflitoDePersistenciaForcado_NaoRepeteChamadaAoCanalERecarregaEstadoPersistido()
    {
        // Força deterministicamente o branch de conflito otimista em
        // SaveTransitionAsync (sem depender de orquestrar threads reais) e
        // comprova que o caso de uso nunca relança a exceção ao chamador nem
        // repete a chamada ao canal — apenas recarrega o estado já
        // persistido para a chave.
        var repository = new FakeTriagemRepository { ConflictsToSimulateOnSaveTransition = 1 };
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var channel = new FakeTriagemChannel();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery, channel: channel);

        var result = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, result.Outcome);
        Assert.Equal(1, channel.RealSendCount);
        Assert.Equal(1, repository.SaveTransitionCallCount);
        // Nada mais persistiu uma transição concorrente de verdade neste
        // teste determinístico — o reload reflete o próprio registro criado
        // (ainda AguardandoEnvio), provando apenas que o caminho de conflito
        // é tratado sem lançar e sem repetir o canal. O cenário em que o
        // reload reflete um vencedor DIFERENTE é coberto pelo teste de
        // concorrência real (50 chamadas) e por
        // FakeTriagemRepositoryTests.
        Assert.Equal(ETriagemStatus.AguardandoEnvio, result.Status);
    }
}
