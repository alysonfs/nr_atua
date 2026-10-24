using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.Triagens;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes dos casos de uso do pool de triagem — épico "Pool de triagem —
/// visão do operador", blocos C1 (lista), C2 (detalhe) e C3 (assumir).
/// Cobrem os critérios de aceite 1, 2, 3, 4, 7, 8 e 10 (ver
/// <c>docs/features/triagem-pool-operador.md</c>).
/// </summary>
public class TriagemPoolUseCasesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    // -------------------------------------------------------------------
    // C1 — Lista do pool
    // -------------------------------------------------------------------

    [Fact]
    public async Task List_RetornaApenasOsQuatroEstadosDoPoolDoTenantComColunasMinimas()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemDoPoolId;
        Guid workOrderIdDoPoolItem;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "Cliente A",
                "Geladeira", "Brastemp");
            workOrderIdDoPoolItem = workOrderId;

            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool);
            triagemDoPoolId = triagem.Id;
            AddTriagem(context, triagem);

            // Fora do pool — nunca deve aparecer na lista (critério de aceite 3).
            var emColeta = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.EmColeta);
            AddTriagem(context, emColeta);

            // Outro tenant — isolamento obrigatório (critério de aceite 2).
            var outroTenantId = await SeedTenantWithMembershipAsync(context, Guid.CreateVersion7());
            var outraIntegrationId = await SeedIntegrationAsync(context, outroTenantId);
            var outroWorkOrderId = await SeedWorkOrderAsync(context, outroTenantId, outraIntegrationId, "X", "Y",
                "Z");
            AddTriagem(context, CriarTriagemNoEstado(outroTenantId, outraIntegrationId, outroWorkOrderId,
                ETriagemStatus.LiberadaAoPool));

            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new ListTriagemPoolUseCase(assertContext);

        var result = await useCase.ExecuteAsync(new TriagemPoolListQuery(tenantId, userId, StatusFilter: null),
            CancellationToken.None);

        Assert.Equal(ETriagemPoolListOutcome.Success, result.Outcome);
        Assert.Equal(1, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(triagemDoPoolId, item.TriagemId);
        Assert.Equal("Cliente A", item.CustomerName);
        Assert.Equal("Geladeira", item.ProductModel);
        Assert.Equal("Brastemp", item.ProductBrand);
        Assert.Equal(ETriagemStatus.LiberadaAoPool, item.Status);
        Assert.Equal(5, item.ChecklistItemsCompleted);
        Assert.Equal(Triagem.ChecklistItensMinimosTotal, item.ChecklistItemsTotal);
        Assert.Equal(workOrderIdDoPoolItem, item.WorkOrderId);
        Assert.Equal(Now, item.StartedAtUtc);
        Assert.Equal(Now, item.FinishedAtUtc);
    }

    [Fact]
    public async Task List_UsuarioNaoMembroDoTenant_RetornaForbidden()
    {
        var databaseName = Guid.NewGuid().ToString();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, Guid.CreateVersion7());
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new ListTriagemPoolUseCase(assertContext);

        var result = await useCase.ExecuteAsync(
            new TriagemPoolListQuery(tenantId, Guid.CreateVersion7(), StatusFilter: null), CancellationToken.None);

        Assert.Equal(ETriagemPoolListOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task List_FiltroPorEstado_RetornaApenasOEstadoPedido()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "Cliente", "P", "M");

            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool));
            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.Expirada));
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new ListTriagemPoolUseCase(assertContext);

        var result = await useCase.ExecuteAsync(
            new TriagemPoolListQuery(tenantId, userId, ETriagemStatus.Expirada), CancellationToken.None);

        Assert.Equal(ETriagemPoolListOutcome.Success, result.Outcome);
        var item = Assert.Single(result.Items);
        Assert.Equal(ETriagemStatus.Expirada, item.Status);
    }

    [Fact]
    public async Task List_FiltroForaDosEstadosDoPool_RetornaInvalidStatusFilter()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new ListTriagemPoolUseCase(assertContext);

        var result = await useCase.ExecuteAsync(
            new TriagemPoolListQuery(tenantId, userId, ETriagemStatus.EmColeta), CancellationToken.None);

        Assert.Equal(ETriagemPoolListOutcome.InvalidStatusFilter, result.Outcome);
    }

    [Fact]
    public async Task List_MensagemDeConsumidorAposEntradaNoPool_MarcaNovaAtividade()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "Cliente", "P", "M");

            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);

            context.TriagemInboundMessages.Add(new TriagemInboundMessage(
                Guid.CreateVersion7(), tenantId, integrationId, "wamid-1", "idem-1", Guid.CreateVersion7(),
                "5584999998888", "chat", fromMe: false, triagem.UpdatedAtUtc.AddMinutes(5), "text",
                "Já te mandei a foto!", ETriagemInboundMessageOutcome.JaEmColeta, triagem.Id, [],
                triagem.UpdatedAtUtc.AddMinutes(5)));

            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new ListTriagemPoolUseCase(assertContext);

        var result = await useCase.ExecuteAsync(new TriagemPoolListQuery(tenantId, userId, StatusFilter: null),
            CancellationToken.None);

        var item = Assert.Single(result.Items, candidate => candidate.TriagemId == triagemId);
        Assert.True(item.HasNewActivity);
    }

    [Fact]
    public async Task List_EscopoInProgress_RetornaApenasEstadosPrePool()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "Cliente", "P", "M");

            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.AguardandoEnvio));
            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.ContatoIniciado));
            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.EmColeta));
            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId,
                ETriagemStatus.InformacoesSuficientes));
            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool));
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new ListTriagemPoolUseCase(assertContext);

        var result = await useCase.ExecuteAsync(
            new TriagemPoolListQuery(tenantId, userId, StatusFilter: null, ETriagemPoolListScope.InProgress),
            CancellationToken.None);

        Assert.Equal(ETriagemPoolListOutcome.Success, result.Outcome);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(
            Triagem.InProgressStatuses.OrderBy(status => status),
            result.Items.Select(item => item.Status).OrderBy(status => status));
    }

    [Fact]
    public async Task List_EscopoAll_RetornaPrePoolEPool()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "Cliente", "P", "M");

            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.AguardandoEnvio));
            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.EmColeta));
            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.Expirada));
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new ListTriagemPoolUseCase(assertContext);

        var result = await useCase.ExecuteAsync(
            new TriagemPoolListQuery(tenantId, userId, StatusFilter: null, ETriagemPoolListScope.All),
            CancellationToken.None);

        Assert.Equal(ETriagemPoolListOutcome.Success, result.Outcome);
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, item => item.Status == ETriagemStatus.EmColeta);
        Assert.Contains(result.Items, item => item.Status == ETriagemStatus.Expirada);
    }

    [Theory]
    [InlineData(ETriagemPoolListScope.InProgress, ETriagemStatus.LiberadaAoPool)]
    [InlineData(ETriagemPoolListScope.InProgress, ETriagemStatus.AguardandoEnvio)]
    [InlineData(ETriagemPoolListScope.Pool, ETriagemStatus.InformacoesSuficientes)]
    [InlineData(ETriagemPoolListScope.All, ETriagemStatus.AssumidaPorOperador)]
    public async Task List_FiltroForaDoEscopo_RetornaInvalidStatusFilter(ETriagemPoolListScope scope,
        ETriagemStatus statusFilter)
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new ListTriagemPoolUseCase(assertContext);

        var result = await useCase.ExecuteAsync(
            new TriagemPoolListQuery(tenantId, userId, statusFilter, scope), CancellationToken.None);

        Assert.Equal(ETriagemPoolListOutcome.InvalidStatusFilter, result.Outcome);
    }

    [Fact]
    public async Task List_EscopoInProgressComFiltroValido_RetornaApenasOEstadoPedido()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "Cliente", "P", "M");

            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.ContatoIniciado));
            AddTriagem(context, CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.EmColeta));
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new ListTriagemPoolUseCase(assertContext);

        var result = await useCase.ExecuteAsync(
            new TriagemPoolListQuery(tenantId, userId, ETriagemStatus.EmColeta, ETriagemPoolListScope.InProgress),
            CancellationToken.None);

        Assert.Equal(ETriagemPoolListOutcome.Success, result.Outcome);
        var item = Assert.Single(result.Items);
        Assert.Equal(ETriagemStatus.EmColeta, item.Status);
    }

    // -------------------------------------------------------------------
    // C2 — Detalhe
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(ETriagemStatus.ContatoIniciado)]
    [InlineData(ETriagemStatus.EmColeta)]
    [InlineData(ETriagemStatus.InformacoesSuficientes)]
    public async Task Detail_TriagemPrePool_RetornaSuccessSomenteLeitura(ETriagemStatus estado)
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "Cliente", "P", "M");
            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, estado);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateDetailUseCase(assertContext);

        var result = await useCase.ExecuteAsync(tenantId, triagemId, userId, CancellationToken.None);

        Assert.Equal(ETriagemPoolDetailOutcome.Success, result.Outcome);
        Assert.Equal("Cliente", result.Detail!.WorkOrder.CustomerName);
    }

    [Fact]
    public async Task Detail_AguardandoEnvio_RetornaNotFound()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "Cliente", "P", "M");
            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.AguardandoEnvio);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateDetailUseCase(assertContext);

        var result = await useCase.ExecuteAsync(tenantId, triagemId, userId, CancellationToken.None);

        Assert.Equal(ETriagemPoolDetailOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Detail_OutroTenant_RetornaNotFound()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;
        Guid outroTenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            outroTenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, outroTenantId);
            var workOrderId = await SeedWorkOrderAsync(context, outroTenantId, integrationId, "C", "P", "M");
            var triagem = CriarTriagemNoEstado(outroTenantId, integrationId, workOrderId,
                ETriagemStatus.LiberadaAoPool);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateDetailUseCase(assertContext);

        var result = await useCase.ExecuteAsync(tenantId, triagemId, userId, CancellationToken.None);

        Assert.Equal(ETriagemPoolDetailOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Detail_UsuarioNaoMembro_RetornaForbidden()
    {
        var databaseName = Guid.NewGuid().ToString();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, Guid.CreateVersion7());
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateDetailUseCase(assertContext);

        var result = await useCase.ExecuteAsync(tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            CancellationToken.None);

        Assert.Equal(ETriagemPoolDetailOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Detail_AssumidaPorOutroOperadorNaoGestor_RetornaNotFound()
    {
        var databaseName = Guid.NewGuid().ToString();
        var solicitanteId = Guid.CreateVersion7();
        var assumidorId = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, solicitanteId, ETenantMembershipRole.Technical);
            context.TenantMemberships.Add(new TenantMembership(tenantId, assumidorId, ETenantMembershipRole.Technical));
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "C", "P", "M");

            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool);
            triagem.AssumirPorOperador(assumidorId, Now);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateDetailUseCase(assertContext);

        var result = await useCase.ExecuteAsync(tenantId, triagemId, solicitanteId, CancellationToken.None);

        Assert.Equal(ETriagemPoolDetailOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Detail_AssumidaPorQuemAssumiu_RetornaSuccessComChecklistEAnexos()
    {
        var databaseName = Guid.NewGuid().ToString();
        var assumidorId = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, assumidorId, ETenantMembershipRole.Technical);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "Cliente", "Fogão",
                "Consul");

            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool);
            triagem.AssumirPorOperador(assumidorId, Now);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);

            context.TriagemInboundMedia.Add(new TriagemInboundMedia(
                Guid.CreateVersion7(), tenantId, integrationId, "wamid-media-1", "idem-media-1",
                Guid.CreateVersion7(), "5584999998888", "chat", fromMe: false, Now.AddMinutes(-10),
                ETriagemInboundMediaKind.Image, "image/jpeg", 1024, "sha", isVoiceNote: false, durationSeconds: null,
                caption: null, ETriagemInboundMediaOutcome.Armazenada, triagemId, [],
                $"tenants/{tenantId:D}/inbound-media/{Guid.CreateVersion7():D}", Now.AddMinutes(-10)));

            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateDetailUseCase(assertContext);

        var result = await useCase.ExecuteAsync(tenantId, triagemId, assumidorId, CancellationToken.None);

        Assert.Equal(ETriagemPoolDetailOutcome.Success, result.Outcome);
        Assert.NotNull(result.Detail);
        Assert.Equal("Cliente", result.Detail!.WorkOrder.CustomerName);
        Assert.Equal(Triagem.ChecklistItensMinimosTotal, result.Detail.Checklist.Count(item => item.Required));
        Assert.Equal(result.Detail.WorkOrder.WorkOrderId, result.Detail.WorkOrderId);
        Assert.Equal(Now, result.Detail.StartedAtUtc);
        Assert.Equal(Now, result.Detail.FinishedAtUtc);
        var anexo = Assert.Single(result.Detail.Attachments);
        Assert.StartsWith("https://fake-triagem-inbound-media.local/", anexo.Url.ToString());
    }

    [Fact]
    public async Task Detail_AssumidaPorOutroOperadorMasSolicitanteEGestor_RetornaSuccess()
    {
        var databaseName = Guid.NewGuid().ToString();
        var gestorId = Guid.CreateVersion7();
        var assumidorId = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, gestorId, ETenantMembershipRole.Owner);
            context.TenantMemberships.Add(new TenantMembership(tenantId, assumidorId, ETenantMembershipRole.Technical));
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "C", "P", "M");

            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool);
            triagem.AssumirPorOperador(assumidorId, Now);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateDetailUseCase(assertContext);

        var result = await useCase.ExecuteAsync(tenantId, triagemId, gestorId, CancellationToken.None);

        Assert.Equal(ETriagemPoolDetailOutcome.Success, result.Outcome);
    }

    // -------------------------------------------------------------------
    // C3 — Assumir
    // -------------------------------------------------------------------

    [Fact]
    public async Task Assume_APartirDoPool_RegistraOperadorERenovaToken()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "C", "P", "M");
            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);
            await context.SaveChangesAsync();
        }

        await using var context2 = CreateContext(databaseName);
        var useCase = new AssumeTriagemUseCase(context2, new TriagemRepository(context2),
            new FakeTimeProvider(Now));

        var result = await useCase.ExecuteAsync(tenantId, triagemId, userId, CancellationToken.None);

        Assert.Equal(EAssumeTriagemOutcome.Success, result.Outcome);
        Assert.Equal(userId, result.CurrentAssumidaPorUserId);
        Assert.Equal(Now, result.CurrentAssumidaEmUtc);
        Assert.Equal(ETriagemStatus.AssumidaPorOperador, result.CurrentStatus);
    }

    [Fact]
    public async Task Assume_UsuarioNaoMembro_RetornaForbidden()
    {
        var databaseName = Guid.NewGuid().ToString();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, Guid.CreateVersion7());
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new AssumeTriagemUseCase(assertContext, new TriagemRepository(assertContext),
            new FakeTimeProvider(Now));

        var result = await useCase.ExecuteAsync(tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            CancellationToken.None);

        Assert.Equal(EAssumeTriagemOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Assume_TriagemDeOutroTenantOuInexistente_RetornaNotFound()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = new AssumeTriagemUseCase(assertContext, new TriagemRepository(assertContext),
            new FakeTimeProvider(Now));

        var result = await useCase.ExecuteAsync(tenantId, Guid.CreateVersion7(), userId, CancellationToken.None);

        Assert.Equal(EAssumeTriagemOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Assume_SequencialJaAssumidaPorOutroOperador_RetornaNotInPoolComQuemEQuando()
    {
        var databaseName = Guid.NewGuid().ToString();
        var operador1 = Guid.CreateVersion7();
        var operador2 = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, operador1);
            context.TenantMemberships.Add(new TenantMembership(tenantId, operador2, ETenantMembershipRole.Technical));
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "C", "P", "M");
            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);
            await context.SaveChangesAsync();
        }

        // Operador 1 assume primeiro, com sucesso.
        await using (var contextOp1 = CreateContext(databaseName))
        {
            var useCaseOp1 = new AssumeTriagemUseCase(contextOp1, new TriagemRepository(contextOp1),
                new FakeTimeProvider(Now));
            var resultadoOp1 = await useCaseOp1.ExecuteAsync(tenantId, triagemId, operador1, CancellationToken.None);
            Assert.Equal(EAssumeTriagemOutcome.Success, resultadoOp1.Outcome);
        }

        // Operador 2 tenta em seguida — já não está mais no pool.
        await using var contextOp2 = CreateContext(databaseName);
        var useCaseOp2 = new AssumeTriagemUseCase(contextOp2, new TriagemRepository(contextOp2),
            new FakeTimeProvider(Now.AddSeconds(1)));
        var resultadoOp2 = await useCaseOp2.ExecuteAsync(tenantId, triagemId, operador2, CancellationToken.None);

        Assert.Equal(EAssumeTriagemOutcome.NotInPool, resultadoOp2.Outcome);
        Assert.Equal(operador1, resultadoOp2.CurrentAssumidaPorUserId);
        Assert.Equal(Now, resultadoOp2.CurrentAssumidaEmUtc);
    }

    [Fact]
    public async Task Assume_CorridaRealDeConcorrencia_PerdedorRecebeConflitoComQuemVenceu()
    {
        var databaseName = Guid.NewGuid().ToString();
        var operador1 = Guid.CreateVersion7();
        var operador2 = Guid.CreateVersion7();
        Guid tenantId;
        Guid triagemId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, operador1);
            context.TenantMemberships.Add(new TenantMembership(tenantId, operador2, ETenantMembershipRole.Technical));
            var integrationId = await SeedIntegrationAsync(context, tenantId);
            var workOrderId = await SeedWorkOrderAsync(context, tenantId, integrationId, "C", "P", "M");
            var triagem = CriarTriagemNoEstado(tenantId, integrationId, workOrderId, ETriagemStatus.LiberadaAoPool);
            triagemId = triagem.Id;
            AddTriagem(context, triagem);
            await context.SaveChangesAsync();
        }

        // contextPerdedor já consulta (rastreia) a triagem ANTES da
        // mutação concorrente do operador 2 — simula uma leitura que já
        // havia ocorrido quando a outra requisição concorrente venceu a
        // corrida e persistiu primeiro.
        await using var contextPerdedor = CreateContext(databaseName);
        await contextPerdedor.Triagens.SingleAsync(item => item.Id == triagemId);

        await using (var contextVencedor = CreateContext(databaseName))
        {
            var useCaseVencedor = new AssumeTriagemUseCase(contextVencedor, new TriagemRepository(contextVencedor),
                new FakeTimeProvider(Now));
            var resultadoVencedor =
                await useCaseVencedor.ExecuteAsync(tenantId, triagemId, operador2, CancellationToken.None);
            Assert.Equal(EAssumeTriagemOutcome.Success, resultadoVencedor.Outcome);
        }

        var useCasePerdedor = new AssumeTriagemUseCase(contextPerdedor, new TriagemRepository(contextPerdedor),
            new FakeTimeProvider(Now.AddSeconds(1)));
        var resultadoPerdedor =
            await useCasePerdedor.ExecuteAsync(tenantId, triagemId, operador1, CancellationToken.None);

        Assert.Equal(EAssumeTriagemOutcome.ConcurrencyConflict, resultadoPerdedor.Outcome);
        Assert.Equal(operador2, resultadoPerdedor.CurrentAssumidaPorUserId);
        Assert.Equal(ETriagemStatus.AssumidaPorOperador, resultadoPerdedor.CurrentStatus);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static GetTriagemPoolDetailUseCase CreateDetailUseCase(AtuaDbContext context) =>
        new(context, new FakeTriagemInboundMediaStorage(), new FakeTimeProvider(Now));

    private static Triagem CriarTriagemNoEstado(Guid tenantId, Guid integrationId, Guid workOrderId,
        ETriagemStatus estado)
    {
        var triagem = new Triagem(Guid.CreateVersion7(), tenantId, integrationId, workOrderId, Now);
        if (estado == ETriagemStatus.AguardandoEnvio) return triagem;

        triagem.MarcarContatoIniciado(Now);
        if (estado == ETriagemStatus.ContatoIniciado) return triagem;
        if (estado == ETriagemStatus.FalhaDeContato)
        {
            var falha = new Triagem(Guid.CreateVersion7(), tenantId, integrationId, workOrderId, Now);
            falha.MarcarFalhaDeContato(Now);
            return falha;
        }

        triagem.IniciarColeta(Now);
        if (estado == ETriagemStatus.EmColeta) return triagem;
        if (estado == ETriagemStatus.Expirada)
        {
            triagem.Expirar(Now);
            return triagem;
        }

        if (estado == ETriagemStatus.AguardandoIntervencao)
        {
            triagem.RegistrarDesvio(Now);
            triagem.RegistrarDesvio(Now);
            triagem.RegistrarDesvio(Now);
            return triagem;
        }

        PreencherChecklistMinimo(triagem);
        triagem.MarcarInformacoesSuficientes(Now);
        if (estado == ETriagemStatus.InformacoesSuficientes) return triagem;

        triagem.LiberarAoPool(Now);
        return triagem;
    }

    private static void PreencherChecklistMinimo(Triagem triagem)
    {
        triagem.RegistrarDescricaoDefeito("Não gela.", Now);
        triagem.RegistrarNotaFiscalLegivel(Now);
        triagem.RegistrarEtiquetaLegivel("ABC123", "SN-1", Now);
        triagem.RegistrarMidiaDefeito(ETriagemMidiaDefeitoTipo.Foto, Now);
        triagem.RegistrarEndereco("Rua X, 1, Natal/RN", Now);
    }

    private static async Task<Guid> SeedTenantWithMembershipAsync(AtuaDbContext context, Guid userId,
        ETenantMembershipRole role = ETenantMembershipRole.Owner)
    {
        var tenantId = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant(tenantId, "Atua", "11122233000183", "America/Sao_Paulo"));
        context.TenantMemberships.Add(new TenantMembership(tenantId, userId, role));
        await context.SaveChangesAsync();
        return tenantId;
    }

    private static async Task<Guid> SeedIntegrationAsync(AtuaDbContext context, Guid tenantId)
    {
        var providerId = Guid.CreateVersion7();
        var integrationId = Guid.CreateVersion7();
        context.IntegrationProviders.Add(new IntegrationProvider(providerId, "iService", "Fabricante",
            new Uri("https://provider.example.com"), true));
        context.Integrations.Add(new Integration(integrationId, tenantId, providerId, true));
        await context.SaveChangesAsync();
        return integrationId;
    }

    private static async Task<Guid> SeedWorkOrderAsync(AtuaDbContext context, Guid tenantId, Guid integrationId,
        string customerName, string productModel, string productBrand)
    {
        var workOrderId = Guid.CreateVersion7();
        var workOrder = new WorkOrder(workOrderId, tenantId, integrationId, $"OS-{workOrderId}", "Designado", Now);
        workOrder.UpdateDetails(new WorkOrderDetails(
            WorkOrderProviderNo: $"BRWO{workOrderId}",
            ServiceRequestId: null,
            Amount: null,
            ProviderCreatedAt: null,
            ProviderUpdatedAt: null,
            CustomerType: null,
            CustomerName: customerName,
            CustomerCpf: null,
            ContactEmail: null,
            ContactPhone: null,
            ContactName: null,
            Address: null,
            ZipCode: null,
            CountryName: null,
            StateName: null,
            CityName: null,
            ProductBrand: productBrand,
            PdCode: null,
            CategoryId: null,
            ProductCategoryCode: null,
            ProductCode: null,
            ProductModel: productModel,
            ProductStatus: null,
            Symptom: null,
            SymptomRaw: null,
            SubStatus: null,
            NextAppointmentAt: null), Now);
        context.WorkOrders.Add(workOrder);
        await context.SaveChangesAsync();
        return workOrderId;
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>().UseInMemoryDatabase(databaseName).Options);

    /// <summary>
    /// Adiciona a triagem ao contexto já atribuindo a shadow property
    /// obrigatória <c>WorkOrderProviderId</c> (ADR-040) — mesma
    /// responsabilidade de <c>TriagemRepository.GetOrAddAsync</c>, que não é
    /// exercitado diretamente por estes testes de use case. O próprio
    /// <see cref="Triagem.Id"/> é usado como chave estável e única o
    /// suficiente para os cenários de teste.
    /// </summary>
    private static void AddTriagem(AtuaDbContext context, Triagem triagem)
    {
        context.Triagens.Add(triagem);
        context.Entry(triagem)
            .Property(TriagemPersistenceSchema.WorkOrderProviderIdShadowPropertyName)
            .CurrentValue = triagem.Id.ToString();
    }
}
