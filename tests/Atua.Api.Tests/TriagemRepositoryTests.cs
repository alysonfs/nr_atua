using Atua.Api.Application.Triagens;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.Triagens;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de <see cref="TriagemRepository"/> (ADR-040) com o provedor EF Core
/// InMemory.
///
/// Limitação reconhecida: o provedor InMemory não reproduz a corrida real do
/// PostgreSQL — ele não lança <see cref="Npgsql.PostgresException"/>
/// (SQLSTATE 23505) quando duas inserções concorrentes colidem na restrição
/// única, então não há, nesta suíte, um teste que force a arbitragem real
/// por restrição única sob concorrência entre conexões. Essa garantia
/// depende do índice único nomeado (verificado em
/// <see cref="TriagemPersistenceMappingTests"/>) e do classificador
/// <see cref="TriagemRepository.IsIdempotencyUniqueViolation"/>, testado
/// isoladamente aqui com uma <see cref="PostgresException"/> construída
/// diretamente — sem subir um Postgres real só para este teste (fora do
/// escopo da tarefa). A garantia comportamental de "exatamente um
/// Created=true" sob concorrência real de tarefas já é coberta, com
/// <see cref="FakeTriagemRepository"/>, em <c>StartTriagemUseCaseTests</c>.
/// </summary>
public class TriagemRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PrimeiraChamada_InsereCandidataERetornaCreatedTrue()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var repository = new TriagemRepository(context);
        var seed = await SeedAsync(context);
        var key = new TriagemIdempotencyKey(seed.TenantId, seed.IntegrationId, "OS-1");
        var candidate = new Triagem(Guid.NewGuid(), seed.TenantId, seed.IntegrationId, seed.WorkOrderId, Now);

        var result = await repository.GetOrAddAsync(key, candidate, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(candidate.Id, result.Triagem.Id);
    }

    [Fact]
    public async Task ChamadaSequencialComMesmaChave_RetornaMesmaTriagemComCreatedFalse()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var repository = new TriagemRepository(context);
        var seed = await SeedAsync(context);
        var key = new TriagemIdempotencyKey(seed.TenantId, seed.IntegrationId, "OS-1");
        var firstCandidate = new Triagem(Guid.NewGuid(), seed.TenantId, seed.IntegrationId, seed.WorkOrderId, Now);
        var secondCandidate = new Triagem(Guid.NewGuid(), seed.TenantId, seed.IntegrationId, seed.WorkOrderId, Now);

        var first = await repository.GetOrAddAsync(key, firstCandidate, CancellationToken.None);
        var second = await repository.GetOrAddAsync(key, secondCandidate, CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Triagem.Id, second.Triagem.Id);
    }

    [Fact]
    public async Task Reidratacao_PreservaChecklistOpcionaisStatusDesviosETentativasPorItem()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var repository = new TriagemRepository(context);
        var seed = await SeedAsync(context);
        var key = new TriagemIdempotencyKey(seed.TenantId, seed.IntegrationId, "OS-1");
        var candidate = new Triagem(Guid.NewGuid(), seed.TenantId, seed.IntegrationId, seed.WorkOrderId, Now);

        await repository.GetOrAddAsync(key, candidate, CancellationToken.None);

        candidate.MarcarContatoIniciado(Now);
        candidate.IniciarColeta(Now);
        candidate.RegistrarDescricaoDefeito("Não gela", Now);
        candidate.RegistrarNotaFiscalLegivel(Now);
        candidate.RegistrarEtiquetaLegivel("ModeloX", "Serie123", Now);
        candidate.RegistrarMidiaDefeito(ETriagemMidiaDefeitoTipo.Foto, Now);
        candidate.RegistrarEndereco("Rua 1, 100", Now);
        candidate.RegistrarPontoReferencia("Perto da praça", Now);
        candidate.RegistrarLocalizacao("-5.8,-35.2", Now);
        candidate.RegistrarTentativaInvalida(ETriagemChecklistItem.Etiqueta, Now);
        candidate.RegistrarTentativaInvalida(ETriagemChecklistItem.Etiqueta, Now);
        candidate.RegistrarDesvio(Now);
        await context.SaveChangesAsync();

        // Novo contexto/repositório sobre o mesmo banco InMemory: força
        // reidratação a partir do armazenamento, sem reaproveitar a
        // instância já em memória do primeiro contexto.
        await using var rehydrationContext = CreateContext(databaseName);
        var rehydrationRepository = new TriagemRepository(rehydrationContext);
        var rehydrated = (await rehydrationRepository.GetOrAddAsync(key,
            new Triagem(Guid.NewGuid(), seed.TenantId, seed.IntegrationId, seed.WorkOrderId, Now),
            CancellationToken.None)).Triagem;

        Assert.Equal(ETriagemStatus.EmColeta, rehydrated.Status);
        Assert.Equal("Não gela", rehydrated.DescricaoDefeito);
        Assert.True(rehydrated.NotaFiscalLegivel);
        Assert.Equal("ModeloX", rehydrated.EtiquetaModelo);
        Assert.Equal("Serie123", rehydrated.EtiquetaSerie);
        Assert.Equal(ETriagemMidiaDefeitoTipo.Foto, rehydrated.MidiaDefeitoTipo);
        Assert.Equal("Rua 1, 100", rehydrated.EnderecoCompleto);
        Assert.Equal("Perto da praça", rehydrated.PontoReferencia);
        Assert.Equal("-5.8,-35.2", rehydrated.Localizacao);
        Assert.Equal(1, rehydrated.DesviosConsecutivos); // RegistrarDesvio foi a última interação
        Assert.Equal(2, rehydrated.TentativasInvalidasPorItem[ETriagemChecklistItem.Etiqueta]);
    }

    [Fact]
    public async Task EstadoAvancado_NaoEhResetadoAoReconsultarComMesmaChave()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var repository = new TriagemRepository(context);
        var seed = await SeedAsync(context);
        var key = new TriagemIdempotencyKey(seed.TenantId, seed.IntegrationId, "OS-1");
        var candidate = new Triagem(Guid.NewGuid(), seed.TenantId, seed.IntegrationId, seed.WorkOrderId, Now);
        await repository.GetOrAddAsync(key, candidate, CancellationToken.None);

        candidate.MarcarContatoIniciado(Now);
        candidate.IniciarColeta(Now);
        await context.SaveChangesAsync();

        var reentry = new Triagem(Guid.NewGuid(), seed.TenantId, seed.IntegrationId, seed.WorkOrderId, Now);
        var result = await repository.GetOrAddAsync(key, reentry, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(ETriagemStatus.EmColeta, result.Triagem.Status);
    }

    [Fact]
    public async Task ChavesDeTenantsDiferentes_SaoIsoladas()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var repository = new TriagemRepository(context);
        var seedA = await SeedAsync(context, cnpj: "11122233000183");
        var seedB = await SeedAsync(context, cnpj: "11222333000181");

        var candidateA = new Triagem(Guid.NewGuid(), seedA.TenantId, seedA.IntegrationId, seedA.WorkOrderId, Now);
        var candidateB = new Triagem(Guid.NewGuid(), seedB.TenantId, seedB.IntegrationId, seedB.WorkOrderId, Now);

        var resultA = await repository.GetOrAddAsync(
            new TriagemIdempotencyKey(seedA.TenantId, seedA.IntegrationId, "OS-1"), candidateA,
            CancellationToken.None);
        var resultB = await repository.GetOrAddAsync(
            new TriagemIdempotencyKey(seedB.TenantId, seedB.IntegrationId, "OS-1"), candidateB,
            CancellationToken.None);

        Assert.True(resultA.Created);
        Assert.True(resultB.Created);
        Assert.NotEqual(resultA.Triagem.Id, resultB.Triagem.Id);
    }

    [Fact]
    public async Task ChavesDeIntegracoesDiferentesNoMesmoTenant_SaoIsoladas()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var repository = new TriagemRepository(context);
        var seed = await SeedAsync(context);
        var otherProvider = new IntegrationProvider(Guid.CreateVersion7(), $"outro-provedor-{Guid.NewGuid()}",
            "Fabricante", new Uri("https://outro.example.com"), true);
        var otherIntegration = new Integration(Guid.CreateVersion7(), seed.TenantId, otherProvider.Id, true);
        var otherWorkOrder = new WorkOrder(Guid.NewGuid(), seed.TenantId, otherIntegration.Id, "OS-EXT-2", "Open",
            Now);
        context.AddRange(otherProvider, otherIntegration, otherWorkOrder);
        await context.SaveChangesAsync();

        var candidateA = new Triagem(Guid.NewGuid(), seed.TenantId, seed.IntegrationId, seed.WorkOrderId, Now);
        var candidateB = new Triagem(Guid.NewGuid(), seed.TenantId, otherIntegration.Id, otherWorkOrder.Id, Now);

        var resultA = await repository.GetOrAddAsync(
            new TriagemIdempotencyKey(seed.TenantId, seed.IntegrationId, "OS-1"), candidateA,
            CancellationToken.None);
        var resultB = await repository.GetOrAddAsync(
            new TriagemIdempotencyKey(seed.TenantId, otherIntegration.Id, "OS-1"), candidateB,
            CancellationToken.None);

        Assert.True(resultA.Created);
        Assert.True(resultB.Created);
        Assert.NotEqual(resultA.Triagem.Id, resultB.Triagem.Id);
    }

    [Fact]
    public void ClassificadorDeViolacao_CapturaSomenteUnicidade23505DaRestricaoDeIdempotencia()
    {
        var matching = BuildDbUpdateException("23505",
            "uq_triagens_tenant_integration_work_order_provider_id");
        var differentConstraint = BuildDbUpdateException("23505", "uq_work_orders_tenant_provider");
        var differentSqlState = BuildDbUpdateException("23503",
            "uq_triagens_tenant_integration_work_order_provider_id");
        var withoutPostgresInner = new DbUpdateException("falha genérica", new InvalidOperationException());

        Assert.True(TriagemRepository.IsIdempotencyUniqueViolation(matching));
        Assert.False(TriagemRepository.IsIdempotencyUniqueViolation(differentConstraint));
        Assert.False(TriagemRepository.IsIdempotencyUniqueViolation(differentSqlState));
        Assert.False(TriagemRepository.IsIdempotencyUniqueViolation(withoutPostgresInner));
    }

    [Fact]
    public async Task ErroDePersistenciaNaoRelacionado_NaoViraSucesso()
    {
        // Simula uma falha de persistência real, não relacionada à restrição
        // de idempotência: duas triagens com o mesmo Id de chave primária.
        // Sob o provedor InMemory essa colisão aparece como
        // ArgumentException direto da store (não como DbUpdateException
        // envolvendo PostgresException, como aconteceria em produção contra
        // o PostgreSQL real — limitação reconhecida do InMemory). Em ambos
        // os casos, o ponto relevante é o mesmo: o classificador só trata a
        // violação nomeada da restrição de idempotência
        // (<see cref="TriagemRepository.IsIdempotencyUniqueViolation"/>), e
        // qualquer outra falha de persistência deve propagar — nunca virar
        // "Created = false" silenciosamente.
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var repository = new TriagemRepository(context);
        var seed = await SeedAsync(context);

        var conflictingId = Guid.NewGuid();
        var alreadyPersisted = new Triagem(conflictingId, seed.TenantId, seed.IntegrationId, seed.WorkOrderId, Now);
        await repository.GetOrAddAsync(
            new TriagemIdempotencyKey(seed.TenantId, seed.IntegrationId, "OS-JA-EXISTENTE"), alreadyPersisted,
            CancellationToken.None);

        // Novo contexto sobre o mesmo banco InMemory: change tracker limpo,
        // então o conflito só aparece no SaveChanges (duplicidade de chave
        // primária na própria store), e não como InvalidOperationException
        // de rastreamento duplicado dentro do mesmo DbContext.
        await using var secondContext = CreateContext(databaseName);
        var secondRepository = new TriagemRepository(secondContext);

        // Mesmo Id de linha, porém chave de idempotência distinta: o
        // caminho rápido não encontra nada para "OS-1" e tenta inserir,
        // colidindo na chave primária (não na restrição de idempotência).
        var candidateWithDuplicatedId = new Triagem(conflictingId, seed.TenantId, seed.IntegrationId,
            seed.WorkOrderId, Now);

        var exception = await Record.ExceptionAsync(() =>
            secondRepository.GetOrAddAsync(
                new TriagemIdempotencyKey(seed.TenantId, seed.IntegrationId, "OS-1"), candidateWithDuplicatedId,
                CancellationToken.None));

        Assert.NotNull(exception);
        Assert.False(exception is DbUpdateException dbUpdateException
            && TriagemRepository.IsIdempotencyUniqueViolation(dbUpdateException));
    }

    private static DbUpdateException BuildDbUpdateException(string sqlState, string constraintName)
    {
        var postgresException = new PostgresException(
            messageText: "duplicate key value violates unique constraint",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            detail: null,
            hint: null,
            position: 0,
            internalPosition: 0,
            internalQuery: null,
            where: null,
            schemaName: null,
            tableName: "triagens",
            columnName: null,
            dataTypeName: null,
            constraintName: constraintName,
            file: null,
            line: null,
            routine: null);

        return new DbUpdateException("Erro ao salvar triagem.", postgresException);
    }

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid WorkOrderId);

    private static async Task<Seed> SeedAsync(AtuaDbContext context, string cnpj = "11122233000183")
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", cnpj, "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), $"iService-{Guid.NewGuid()}",
            "Fabricante", new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var workOrder = new WorkOrder(Guid.NewGuid(), tenant.Id, integration.Id, "OS-EXT-1", "Open", Now);
        context.AddRange(tenant, provider, integration, workOrder);
        await context.SaveChangesAsync();

        return new Seed(tenant.Id, integration.Id, workOrder.Id);
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(databaseName).Options);
}
