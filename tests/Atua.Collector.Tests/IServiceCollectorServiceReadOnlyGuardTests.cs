using Atua.Collector.IService;

namespace Atua.Collector.Tests;

/// <summary>
/// Testes unitários de <see cref="IServiceCollectorService.IsWriteRequestOnIService"/>
/// (RF-013 — modo somente leitura), fechando DP-013.2: garantia verificável de que o
/// guard de rede classifica corretamente requisições de escrita/leitura, sem depender
/// de um navegador Playwright real.
/// </summary>
public class IServiceCollectorServiceReadOnlyGuardTests
{
    private const string IServiceHost = "https://ics-amer.midea.com";

    [Theory]
    [InlineData("GET", "/web/iservice-wom/workOrder/queryWorkOrder")]
    [InlineData("GET", "/anything/at/all")]
    [InlineData("HEAD", "/web/iservice-wom/workOrder/queryWorkOrder")]
    [InlineData("OPTIONS", "/web/iservice-wom/workOrder/queryWorkOrder")]
    public void MetodosDeLeituraSeguraNuncaSaoBloqueados(string method, string path)
    {
        Assert.False(IServiceCollectorService.IsWriteRequestOnIService(method, IServiceHost + path));
    }

    [Theory]
    [InlineData("/web/iservice-wom/workOrder/queryWorkOrder")]
    [InlineData("/web/iservice-wom/workOrder/queryOneWorkOrder")]
    [InlineData("/web/iservice-wom/workOrder/getStatusCount")]
    [InlineData("/web/iservice-wom/workOrder/selectAssignedTechnicians")]
    public void PostDeConsultaConhecidaNaoEBloqueado(string path)
    {
        Assert.False(IServiceCollectorService.IsWriteRequestOnIService("POST", IServiceHost + path));
    }

    [Theory]
    [InlineData("/web/iservice-wom/workOrder/acceptWorkOrder")]
    [InlineData("/web/iservice-wom/workOrder/reassignTechnician")]
    [InlineData("/web/iservice-wom/workOrder/updateStatus")]
    public void PostDeEscritaConhecidaEBloqueado(string path)
    {
        Assert.True(IServiceCollectorService.IsWriteRequestOnIService("POST", IServiceHost + path));
    }

    [Theory]
    [InlineData("/web/iservice-wom/parts/updateParts")]
    [InlineData("/web/iservice-wom/schedule/reschedule")]
    [InlineData("/web/iservice-wom/customer/sendMessage")]
    public void PostForaDoModuloWorkOrderMasDentroDeIserviceWomTambemEBloqueado_DP013_1(string path)
    {
        // DP-013.1: a cobertura do guard não se limita a /workOrder/ — qualquer POST
        // em outro submódulo de negócio de OS dentro de /web/iservice-wom/ (peças,
        // agendamento, comunicação com cliente) também é bloqueado por padrão
        // (default-deny), mesmo sem heurística de nome de endpoint específica para
        // o submódulo.
        Assert.True(IServiceCollectorService.IsWriteRequestOnIService("POST", IServiceHost + path));
    }

    [Theory]
    [InlineData("POST", "/web/auth-server/login/option")]
    [InlineData("POST", "/web/auth-server/user/getSetProfile")]
    [InlineData("POST", "/web/iservice-admin/htmlAppErrorLog/insertLog")]
    public void PostEmModuloDeInfraestruturaForaDeIserviceWomNaoEBloqueado(string method, string path)
    {
        // Regressão real observada em produção (2026-09-02): um guard default-deny
        // aplicado a TODO o host (não só /web/iservice-wom/) bloqueou endpoints de
        // sessão CAS e telemetria de erro do cliente, quebrando o login do Coletor
        // (outcome=Failed). Esses endpoints não escrevem dados de OS/cliente — o
        // guard deve permanecer escopado a /web/iservice-wom/, não ao host inteiro.
        Assert.False(IServiceCollectorService.IsWriteRequestOnIService(method, IServiceHost + path));
    }

    [Theory]
    [InlineData("PUT", "/web/iservice-wom/workOrder/queryWorkOrder")]
    [InlineData("PATCH", "/web/iservice-wom/workOrder/queryWorkOrder")]
    [InlineData("DELETE", "/web/iservice-wom/workOrder/queryWorkOrder")]
    [InlineData("PUT", "/web/iservice-wom/parts/anything")]
    public void MetodosDeEscritaNaoSeguraSaoSempreBloqueadosDentroDeIserviceWom(string method, string path)
    {
        // Nunca há motivo legítimo para o Coletor enviar PUT/PATCH/DELETE ao módulo de
        // negócio de OS — bloqueado incondicionalmente, mesmo em caminhos com nome de
        // "query"/"get".
        Assert.True(IServiceCollectorService.IsWriteRequestOnIService(method, IServiceHost + path));
    }

    [Theory]
    [InlineData("POST", "https://signin.midea.com/login")]
    [InlineData("POST", "https://cdn.midea.com/assets/script.js")]
    [InlineData("GET", "https://signin.midea.com/login")]
    public void RequisicoesForaDoHostDoIServiceNuncaSaoBloqueadas(string method, string url)
    {
        // O guard é escopado exclusivamente ao host do iService (RF-013) — o fluxo de
        // login CAS (signin.midea.com) e outros hosts não são afetados.
        Assert.False(IServiceCollectorService.IsWriteRequestOnIService(method, url));
    }

    [Theory]
    [InlineData("POST", "not-a-valid-url")]
    [InlineData("POST", "")]
    public void UrlInvalidaOuVaziaNaoEBloqueada(string method, string url)
    {
        Assert.False(IServiceCollectorService.IsWriteRequestOnIService(method, url));
    }
}
