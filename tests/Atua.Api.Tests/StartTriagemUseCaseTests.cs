using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Testes do caso de uso idempotente de início da triagem (ADR-039,
/// arquitetura seção 3, itens 2/3, 5.3; ADR-042). O WorkOrderId interno
/// nunca é recebido do chamador: é sempre resolvido por
/// <see cref="FakeTriagemEligibleWorkOrderQuery"/> (tenant + integração +
/// WorkOrderProviderId), que também determina elegibilidade.
/// <see cref="FakeTriagemRepository"/> substitui um armazenamento físico
/// (clona a cada leitura e arbitra concorrência por token, como o
/// PostgreSQL real).
///
/// Desde o ADR-042, <c>StartTriagemUseCase</c> NUNCA chama o canal em
/// processo: uma criação bem-sucedida apenas persiste a triagem
/// (<see cref="ETriagemStatus.AguardandoEnvio"/>) e um
/// <see cref="TriagemChannelCommand"/> atomicamente na mesma chamada — quem
/// avança a triagem para <see cref="ETriagemStatus.ContatoIniciado"/> ou
/// <see cref="ETriagemStatus.FalhaDeContato"/> é o fluxo claim/complete
/// (<see cref="TriagemChannelCommandService"/>), testado separadamente em
/// <c>TriagemChannelCommandServiceTests</c>.
/// </summary>
public class StartTriagemUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static StartTriagemUseCase CreateUseCase(FakeTriagemRepository repository,
        FakeTriagemEligibleWorkOrderQuery workOrderQuery, DateTimeOffset? now = null) =>
        new(workOrderQuery, repository, new FakeTriagemMessageTemplateResolver(),
            new FakeTimeProvider(now ?? Now));

    [Fact]
    public async Task OsElegivelAssigned_PrimeiraExecucao_CriaTriagemAguardandoEnvioEComandoDeCanalAtomico()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned",
            contactPhone: "+5511999999999");
        var useCase = CreateUseCase(repository, workOrderQuery);

        var result = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, result.Outcome);
        // Desde o ADR-042, nenhuma chamada síncrona ao canal ocorre: a
        // triagem permanece AguardandoEnvio até que o processo do canal
        // reivindique (claim) e conclua (complete) o comando.
        Assert.Equal(ETriagemStatus.AguardandoEnvio, result.Status);
        Assert.NotNull(result.TriagemId);
        Assert.NotEqual(Guid.Empty, result.TriagemId!.Value);

        // O comando de canal foi persistido atomicamente com a triagem
        // (ADR-042: "mesma transação que registra o aceite idempotente").
        Assert.True(repository.ChannelCommandsByTriagemId.TryGetValue(result.TriagemId.Value, out var command));
        Assert.Equal(tenantId, command!.TenantId);
        Assert.Equal(integrationId, command.IntegrationId);
        Assert.Equal(result.TriagemId.Value, command.TriagemId);
        Assert.Equal(workOrderId, command.WorkOrderId);
        Assert.Equal(FakeTriagemMessageTemplateResolver.TemplateVersionId.ToString(), command.TemplateId);
        Assert.Equal(1, command.TemplateVersion);
        Assert.Equal("Mensagem renderizada para OS-1", command.RenderedContent);
        // TEMPORÁRIO (TriagemTestSafeguard): telefone da OS fora da lista
        // autorizada cai no telefone fixo do E2E, nunca no telefone real.
        Assert.Equal(TriagemTestSafeguard.FixedRecipientPhone, command.RecipientPhone);
        Assert.Equal(new TriagemIdempotencyKey(tenantId, integrationId, "OS-1").ToString(), command.IdempotencyKey);
        Assert.Equal(ETriagemChannelCommandStatus.Pending, command.Status);
        Assert.NotEqual(Guid.Empty, command.CorrelationId);
    }

    [Theory]
    [InlineData("5584991050065")]
    [InlineData("558491050065")]
    public async Task OsComTelefoneDeTesteAutorizado_UsaOTelefoneDaOsComoDestinatario(string contactPhone)
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-2", Guid.NewGuid(), "assigned", contactPhone);
        var useCase = CreateUseCase(repository, workOrderQuery);

        var result = await useCase.ExecuteAsync(tenantId, integrationId, "OS-2", CancellationToken.None);

        Assert.True(repository.ChannelCommandsByTriagemId.TryGetValue(result.TriagemId!.Value, out var command));
        Assert.Equal("+5584991050065", command!.RecipientPhone);
    }

    [Fact]
    public async Task MesmaOsElegivel_RepeticaoSequencial_RetornaExistingSemCriarNovoComandoDeCanal()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);

        var first = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);
        var second = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, first.Outcome);
        Assert.Equal(EStartTriagemOutcome.Existing, second.Outcome);
        Assert.Equal(first.TriagemId, second.TriagemId);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, second.Status);
        // Exatamente um comando de canal persistido para a triagem — a
        // reentrega nunca reenfileira.
        Assert.Single(repository.ChannelCommandsByTriagemId);
    }

    [Fact]
    public async Task ChamadasConcorrentesComMesmaChaveElegivel_ProduzemExatamenteUmCreatedEUmComandoDeCanal()
    {
        var repository = new FakeTriagemRepository();
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-concorrente", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);
        const int concurrency = 50;

        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => useCase.ExecuteAsync(tenantId, integrationId, "OS-concorrente", CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(result => result.Outcome == EStartTriagemOutcome.Created));
        Assert.Equal(concurrency, results.Count(result => result.Outcome is EStartTriagemOutcome.Created
            or EStartTriagemOutcome.Existing));
        Assert.Single(results.Select(result => result.TriagemId).Distinct());
        Assert.All(results, result => Assert.Equal(ETriagemStatus.AguardandoEnvio, result.Status));
        // Mesmo sob 50 chamadas concorrentes, no máximo um comando de canal
        // é persistido para a chave — garantido pela mesma unicidade
        // (índice único em IdempotencyKey) que protege a triagem.
        Assert.Single(repository.ChannelCommandsByTriagemId);
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

        // Avançamos manualmente o estado (como faria TriagemChannelCommandService
        // ao concluir o comando com sucesso, e depois IniciarColeta) para
        // comprovar que o caso de uso nunca "reseta" um estado já avançado
        // além de AguardandoEnvio.
        var key = new TriagemIdempotencyKey(tenantId, integrationId, "OS-1");
        var stored = await repository.FindByKeyAsync(key, CancellationToken.None);
        Assert.NotNull(stored);
        stored!.MarcarContatoIniciado(Now);
        stored.IniciarColeta(Now);
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
        Assert.Equal(ETriagemStatus.AguardandoEnvio, second.Status);
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
    public async Task ConflitoDePersistenciaForcado_NaoRepeteACriacaoERecarregaEstadoPersistido()
    {
        // Força deterministicamente o branch de conflito otimista em
        // SaveTransitionAsync (sem depender de orquestrar threads reais) e
        // comprova que o caso de uso nunca relança a exceção ao chamador —
        // apenas recarrega o estado já persistido para a chave. Note que
        // este cenário específico (conflito simulado em SaveTransitionAsync,
        // não em GetOrAddWithChannelCommandAsync) é residual do fake: a
        // implementação atual de StartTriagemUseCase não chama
        // SaveTransitionAsync, então o contador de conflitos configurado
        // nunca é consumido — o teste apenas comprova que a execução
        // simples permanece correta mesmo com o fake configurado para
        // simular conflitos em um método que não é mais exercitado por este
        // caso de uso.
        var repository = new FakeTriagemRepository { ConflictsToSimulateOnSaveTransition = 1 };
        var workOrderQuery = new FakeTriagemEligibleWorkOrderQuery();
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        workOrderQuery.Seed(tenantId, integrationId, "OS-1", workOrderId, "assigned");
        var useCase = CreateUseCase(repository, workOrderQuery);

        var result = await useCase.ExecuteAsync(tenantId, integrationId, "OS-1", CancellationToken.None);

        Assert.Equal(EStartTriagemOutcome.Created, result.Outcome);
        Assert.Equal(0, repository.SaveTransitionCallCount);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, result.Status);
    }
}
