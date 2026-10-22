using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de domínio puro da triagem automatizada via WhatsApp (épico de
/// triagem WhatsApp Fase 1 e ADR-039). Cobrem a máquina de estados, o
/// checklist mínimo, os contadores de tentativa inválida/desvio e a chave de
/// idempotência. Sem persistência, endpoints, fila, canal, LLM, notificações
/// ou timers — fora do escopo deste incremento.
/// </summary>
public class TriagemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static Triagem CriarTriagemNova() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

    private static Triagem CriarTriagemEmColeta()
    {
        var triagem = CriarTriagemNova();
        triagem.MarcarContatoIniciado(Now);
        triagem.IniciarColeta(Now);
        return triagem;
    }

    private static void PreencherChecklistMinimo(Triagem triagem,
        ETriagemMidiaDefeitoTipo midia = ETriagemMidiaDefeitoTipo.Foto)
    {
        triagem.RegistrarDescricaoDefeito("Não gela e faz barulho.", Now);
        triagem.RegistrarNotaFiscalLegivel(Now);
        triagem.RegistrarEtiquetaLegivel("ABC123", "SN-998877", Now);
        triagem.RegistrarMidiaDefeito(midia, Now);
        triagem.RegistrarEndereco("Rua das Flores, 123, Natal/RN", Now);
    }

    // -----------------------------------------------------------------------
    // Caminho feliz completo
    // -----------------------------------------------------------------------

    [Fact]
    public void CaminhoFelizCompleto_TransitaAteAssumidaPorOperador()
    {
        var triagem = CriarTriagemNova();
        Assert.Equal(ETriagemStatus.AguardandoEnvio, triagem.Status);

        triagem.MarcarContatoIniciado(Now);
        Assert.Equal(ETriagemStatus.ContatoIniciado, triagem.Status);

        triagem.IniciarColeta(Now);
        Assert.Equal(ETriagemStatus.EmColeta, triagem.Status);

        PreencherChecklistMinimo(triagem);
        Assert.True(triagem.ChecklistMinimoCompleto);

        triagem.MarcarInformacoesSuficientes(Now);
        Assert.Equal(ETriagemStatus.InformacoesSuficientes, triagem.Status);

        triagem.LiberarAoPool(Now);
        Assert.Equal(ETriagemStatus.LiberadaAoPool, triagem.Status);

        var operadorUserId = Guid.NewGuid();
        triagem.AssumirPorOperador(operadorUserId, Now);
        Assert.Equal(ETriagemStatus.AssumidaPorOperador, triagem.Status);
        Assert.Equal(operadorUserId, triagem.AssumidaPorUserId);
        Assert.Equal(Now, triagem.AssumidaEmUtc);
    }

    // -----------------------------------------------------------------------
    // Checklist mínimo
    // -----------------------------------------------------------------------

    [Fact]
    public void ChecklistIncompleto_BloqueiaInformacoesSuficientes()
    {
        var triagem = CriarTriagemEmColeta();
        triagem.RegistrarDescricaoDefeito("Não liga.", Now);
        triagem.RegistrarNotaFiscalLegivel(Now);
        // Etiqueta, mídia e endereço ficam pendentes.

        Assert.False(triagem.ChecklistMinimoCompleto);
        Assert.Throws<InvalidTriagemTransitionException>(() => triagem.MarcarInformacoesSuficientes(Now));
        Assert.Equal(ETriagemStatus.EmColeta, triagem.Status);
    }

    [Theory]
    [InlineData(ETriagemMidiaDefeitoTipo.Foto)]
    [InlineData(ETriagemMidiaDefeitoTipo.Video)]
    public void FotoOuVideoSatisfazemOItemDeMidia(ETriagemMidiaDefeitoTipo midia)
    {
        var triagem = CriarTriagemEmColeta();
        PreencherChecklistMinimo(triagem, midia);

        Assert.True(triagem.ChecklistMinimoCompleto);
        triagem.MarcarInformacoesSuficientes(Now);
        Assert.Equal(ETriagemStatus.InformacoesSuficientes, triagem.Status);
    }

    [Fact]
    public void ItensOpcionaisAusentes_NaoBloqueiamSuficiencia()
    {
        var triagem = CriarTriagemEmColeta();
        PreencherChecklistMinimo(triagem);
        // Ponto de referência e localização nunca são registrados.

        Assert.True(triagem.ChecklistMinimoCompleto);
        Assert.Null(triagem.PontoReferencia);
        Assert.Null(triagem.Localizacao);

        triagem.MarcarInformacoesSuficientes(Now);
        Assert.Equal(ETriagemStatus.InformacoesSuficientes, triagem.Status);
    }

    // -----------------------------------------------------------------------
    // Expiração e falha de contato
    // -----------------------------------------------------------------------

    [Fact]
    public void Expirar_APartirDeContatoIniciado_TransitaParaExpirada()
    {
        var triagem = CriarTriagemNova();
        triagem.MarcarContatoIniciado(Now);

        triagem.Expirar(Now);

        Assert.Equal(ETriagemStatus.Expirada, triagem.Status);
    }

    [Fact]
    public void Expirar_APartirDeEmColeta_TransitaParaExpirada()
    {
        var triagem = CriarTriagemEmColeta();

        triagem.Expirar(Now);

        Assert.Equal(ETriagemStatus.Expirada, triagem.Status);
    }

    [Fact]
    public void MarcarFalhaDeContato_APartirDeAguardandoEnvio_TransitaParaFalhaDeContato()
    {
        var triagem = CriarTriagemNova();

        triagem.MarcarFalhaDeContato(Now);

        Assert.Equal(ETriagemStatus.FalhaDeContato, triagem.Status);
    }

    // -----------------------------------------------------------------------
    // Tentativas inválidas por item
    // -----------------------------------------------------------------------

    [Fact]
    public void TerceiraTentativaInvalidaDoMesmoItem_TransitaParaAguardandoIntervencao()
    {
        var triagem = CriarTriagemEmColeta();

        triagem.RegistrarTentativaInvalida(ETriagemChecklistItem.NotaFiscal, Now);
        Assert.Equal(ETriagemStatus.EmColeta, triagem.Status);

        triagem.RegistrarTentativaInvalida(ETriagemChecklistItem.NotaFiscal, Now);
        Assert.Equal(ETriagemStatus.EmColeta, triagem.Status);

        triagem.RegistrarTentativaInvalida(ETriagemChecklistItem.NotaFiscal, Now);
        Assert.Equal(ETriagemStatus.AguardandoIntervencao, triagem.Status);
        Assert.Equal(3, triagem.TentativasInvalidasPorItem[ETriagemChecklistItem.NotaFiscal]);
    }

    [Fact]
    public void TentativasInvalidasDeItensDistintos_NaoSeSomam()
    {
        var triagem = CriarTriagemEmColeta();

        triagem.RegistrarTentativaInvalida(ETriagemChecklistItem.NotaFiscal, Now);
        triagem.RegistrarTentativaInvalida(ETriagemChecklistItem.NotaFiscal, Now);
        triagem.RegistrarTentativaInvalida(ETriagemChecklistItem.Etiqueta, Now);

        Assert.Equal(ETriagemStatus.EmColeta, triagem.Status);
        Assert.Equal(2, triagem.TentativasInvalidasPorItem[ETriagemChecklistItem.NotaFiscal]);
        Assert.Equal(1, triagem.TentativasInvalidasPorItem[ETriagemChecklistItem.Etiqueta]);
    }

    // -----------------------------------------------------------------------
    // Desvios de contexto
    // -----------------------------------------------------------------------

    [Fact]
    public void TerceiroDesvioConsecutivo_TransitaParaAguardandoIntervencao()
    {
        var triagem = CriarTriagemEmColeta();

        triagem.RegistrarDesvio(Now);
        Assert.Equal(ETriagemStatus.EmColeta, triagem.Status);

        triagem.RegistrarDesvio(Now);
        Assert.Equal(ETriagemStatus.EmColeta, triagem.Status);

        triagem.RegistrarDesvio(Now);
        Assert.Equal(ETriagemStatus.AguardandoIntervencao, triagem.Status);
    }

    [Fact]
    public void InteracaoValida_ZeraDesviosConsecutivos()
    {
        var triagem = CriarTriagemEmColeta();

        triagem.RegistrarDesvio(Now);
        triagem.RegistrarDesvio(Now);
        Assert.Equal(2, triagem.DesviosConsecutivos);

        triagem.RegistrarDescricaoDefeito("Não gela.", Now);
        Assert.Equal(0, triagem.DesviosConsecutivos);

        // Novos desvios após o reset não alcançam o limite sozinhos.
        triagem.RegistrarDesvio(Now);
        triagem.RegistrarDesvio(Now);
        Assert.Equal(ETriagemStatus.EmColeta, triagem.Status);
    }

    // -----------------------------------------------------------------------
    // Estados terminais e transições inválidas
    // -----------------------------------------------------------------------

    [Fact]
    public void Expirada_NaoTransitaParaAguardandoIntervencao()
    {
        var triagem = CriarTriagemEmColeta();
        triagem.Expirar(Now);
        Assert.Equal(ETriagemStatus.Expirada, triagem.Status);

        Assert.Throws<InvalidTriagemTransitionException>(() => triagem.RegistrarDesvio(Now));
        Assert.Throws<InvalidTriagemTransitionException>(
            () => triagem.RegistrarTentativaInvalida(ETriagemChecklistItem.NotaFiscal, Now));

        Assert.Equal(ETriagemStatus.Expirada, triagem.Status);
    }

    [Fact]
    public void TransicaoInvalida_PreservaEstadoAtual()
    {
        var triagem = CriarTriagemNova();

        Assert.Throws<InvalidTriagemTransitionException>(() => triagem.AssumirPorOperador(Guid.NewGuid(), Now));
        Assert.Equal(ETriagemStatus.AguardandoEnvio, triagem.Status);

        Assert.Throws<InvalidTriagemTransitionException>(() => triagem.IniciarColeta(Now));
        Assert.Equal(ETriagemStatus.AguardandoEnvio, triagem.Status);

        triagem.MarcarContatoIniciado(Now);
        Assert.Throws<InvalidTriagemTransitionException>(() => triagem.MarcarFalhaDeContato(Now));
        Assert.Equal(ETriagemStatus.ContatoIniciado, triagem.Status);
    }

    // -----------------------------------------------------------------------
    // Chave de idempotência
    // -----------------------------------------------------------------------

    [Fact]
    public void TriagemIdempotencyKey_IgualdadePorValor()
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();

        var chaveA = new TriagemIdempotencyKey(tenantId, integrationId, "BRWO260909869");
        var chaveB = new TriagemIdempotencyKey(tenantId, integrationId, "BRWO260909869");
        var chaveComOutraOs = new TriagemIdempotencyKey(tenantId, integrationId, "BRWO000000001");

        Assert.Equal(chaveA, chaveB);
        Assert.Equal(chaveA.GetHashCode(), chaveB.GetHashCode());
        Assert.NotEqual(chaveA, chaveComOutraOs);
    }

    [Fact]
    public void TriagemIdempotencyKey_NormalizaEspacosNaOs()
    {
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();

        var chaveA = new TriagemIdempotencyKey(tenantId, integrationId, "BRWO260909869");
        var chaveB = new TriagemIdempotencyKey(tenantId, integrationId, "  BRWO260909869  ");

        Assert.Equal(chaveA, chaveB);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TriagemIdempotencyKey_RejeitaWorkOrderProviderIdInvalido(string? workOrderProviderId)
    {
        Assert.Throws<ArgumentException>(
            () => new TriagemIdempotencyKey(Guid.NewGuid(), Guid.NewGuid(), workOrderProviderId!));
    }

    [Fact]
    public void TriagemIdempotencyKey_RejeitaTenantOuIntegrationVazios()
    {
        Assert.Throws<ArgumentException>(
            () => new TriagemIdempotencyKey(Guid.Empty, Guid.NewGuid(), "BRWO260909869"));
        Assert.Throws<ArgumentException>(
            () => new TriagemIdempotencyKey(Guid.NewGuid(), Guid.Empty, "BRWO260909869"));
    }

    // -----------------------------------------------------------------------
    // AssumirPorOperador — pool de triagem (épico, bloco C)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(ETriagemStatus.LiberadaAoPool)]
    [InlineData(ETriagemStatus.Expirada)]
    [InlineData(ETriagemStatus.FalhaDeContato)]
    [InlineData(ETriagemStatus.AguardandoIntervencao)]
    public void AssumirPorOperador_PermiteAPartirDeQualquerEstadoDoPool(ETriagemStatus estadoDoPool)
    {
        var triagem = CriarTriagemNoEstado(estadoDoPool);
        var operadorUserId = Guid.NewGuid();

        triagem.AssumirPorOperador(operadorUserId, Now);

        Assert.Equal(ETriagemStatus.AssumidaPorOperador, triagem.Status);
        Assert.Equal(operadorUserId, triagem.AssumidaPorUserId);
        Assert.Equal(Now, triagem.AssumidaEmUtc);
    }

    [Theory]
    [InlineData(ETriagemStatus.AguardandoEnvio)]
    [InlineData(ETriagemStatus.ContatoIniciado)]
    [InlineData(ETriagemStatus.EmColeta)]
    [InlineData(ETriagemStatus.InformacoesSuficientes)]
    public void AssumirPorOperador_RejeitaEstadosForaDoPool(ETriagemStatus estadoForaDoPool)
    {
        var triagem = CriarTriagemNoEstado(estadoForaDoPool);

        Assert.Throws<InvalidTriagemTransitionException>(
            () => triagem.AssumirPorOperador(Guid.NewGuid(), Now));
        Assert.Equal(estadoForaDoPool, triagem.Status);
        Assert.Null(triagem.AssumidaPorUserId);
        Assert.Null(triagem.AssumidaEmUtc);
    }

    [Fact]
    public void AssumirPorOperador_RejeitaOperadorVazio()
    {
        var triagem = CriarTriagemNoEstado(ETriagemStatus.LiberadaAoPool);

        Assert.Throws<ArgumentException>(() => triagem.AssumirPorOperador(Guid.Empty, Now));
        Assert.Equal(ETriagemStatus.LiberadaAoPool, triagem.Status);
    }

    [Fact]
    public void AssumirPorOperador_RenovaConcurrencyTokenEAtualizaUpdatedAt()
    {
        var triagem = CriarTriagemNoEstado(ETriagemStatus.LiberadaAoPool);
        var tokenAntes = triagem.ConcurrencyToken;
        var depois = Now.AddMinutes(5);

        triagem.AssumirPorOperador(Guid.NewGuid(), depois);

        Assert.NotEqual(tokenAntes, triagem.ConcurrencyToken);
        Assert.Equal(depois, triagem.UpdatedAtUtc);
    }

    /// <summary>
    /// Constrói uma triagem já no estado solicitado, percorrendo as
    /// transições de domínio válidas (nunca via reflexão/atribuição direta).
    /// </summary>
    private static Triagem CriarTriagemNoEstado(ETriagemStatus estado)
    {
        var triagem = CriarTriagemNova();
        if (estado == ETriagemStatus.AguardandoEnvio) return triagem;

        triagem.MarcarContatoIniciado(Now);
        if (estado == ETriagemStatus.ContatoIniciado) return triagem;
        if (estado == ETriagemStatus.FalhaDeContato)
        {
            // FalhaDeContato só é alcançável a partir de AguardandoEnvio —
            // reconstrói a triagem para esse caminho específico.
            var falha = CriarTriagemNova();
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
}
