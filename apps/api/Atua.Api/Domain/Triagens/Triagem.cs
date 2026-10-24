namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Triagem automatizada de uma OS via WhatsApp (épico de triagem WhatsApp
/// Fase 1). Controla os 9 estados da máquina de estados (seção 5 do épico;
/// seção 4 da arquitetura — a Master API é a autoridade da máquina de
/// estados).
///
/// Este incremento cobre somente o domínio puro: transições de estado,
/// checklist mínimo e contadores de tentativa inválida/desvio. Persistência,
/// endpoints, fila, canal, LLM, notificações e timers ficam para incrementos
/// futuros.
/// </summary>
public sealed class Triagem
{
    /// <summary>Limite de tentativas inválidas do mesmo item antes de <see cref="ETriagemStatus.AguardandoIntervencao"/> (pergunta 12 resolvida em 2026-10-13).</summary>
    public const int MaxTentativasInvalidasPorItem = 3;

    /// <summary>Limite de desvios consecutivos antes de <see cref="ETriagemStatus.AguardandoIntervencao"/> (pergunta 11 resolvida em 2026-10-13).</summary>
    public const int MaxDesviosConsecutivos = 3;

    private readonly Dictionary<ETriagemChecklistItem, int> _tentativasInvalidasPorItem = new();

    private Triagem() { }

    public Triagem(Guid id, Guid tenantId, Guid integrationId, Guid workOrderId, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        WorkOrderId = workOrderId;
        Status = ETriagemStatus.AguardandoEnvio;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Redundante para isolamento multi-tenant, mesmo padrão de <see cref="Atua.Api.Domain.WorkOrders.WorkOrder"/>.</summary>
    public Guid TenantId { get; private set; }

    public Guid IntegrationId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public ETriagemStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Momento em que a coleta da LLM com o consumidor terminou (saiu do
    /// loop conversacional) — usado pelo Office para calcular o tempo de
    /// triagem (tempo de resposta da LLM). Preenchida apenas na primeira
    /// vez (nunca sobrescrita) em <see cref="LiberarAoPool"/>,
    /// <see cref="MarcarAguardandoIntervencao"/>, <see cref="Expirar"/> e
    /// <see cref="MarcarFalhaDeContato"/>.
    ///
    /// Pendência conhecida: <see cref="RegistrarTentativaInvalida"/> e
    /// <see cref="RegistrarDesvio"/> também podem mover a triagem para
    /// <see cref="ETriagemStatus.AguardandoIntervencao"/> diretamente (sem
    /// passar por <see cref="MarcarAguardandoIntervencao"/>) — nesse caminho
    /// este campo não é preenchido no momento da transição (reportado ao
    /// orchestrator/software-architect; fica para a migration de backfill
    /// cobrir o histórico existente).
    /// </summary>
    public DateTimeOffset? ColetaEncerradaEmUtc { get; private set; }

    /// <summary>
    /// Token de concorrência otimista (mesmo padrão de
    /// <see cref="Atua.Api.Domain.Integrations.CollectorControl.CollectorActivation.ConcurrencyToken"/>
    /// e de <see cref="Atua.Api.Domain.Integrations.CollectorControl.ImmediateCollectionCommand.ConcurrencyToken"/>).
    /// Regenerado a cada mutação — garante que duas persistências
    /// concorrentes da mesma triagem (ex.: resolução de corrida de
    /// <c>GetOrAddAsync</c> seguida de envio do template por dois
    /// chamadores) sejam arbitradas pelo banco, nunca por um lock em
    /// memória.
    /// </summary>
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    /// <summary>Operador que assumiu a triagem (<see cref="ETriagemStatus.AssumidaPorOperador"/>); <c>null</c> enquanto não assumida.</summary>
    public Guid? AssumidaPorUserId { get; private set; }

    /// <summary>Momento da assunção (auditoria — épico, seção "C3 — Assumir").</summary>
    public DateTimeOffset? AssumidaEmUtc { get; private set; }

    // ------------------------------------------------------------------
    // Checklist mínimo (épico, pergunta 10 resolvida em 2026-10-13).
    // Somente metadados são armazenados — nenhum binário de mídia.
    // ------------------------------------------------------------------

    public string? DescricaoDefeito { get; private set; }

    public bool NotaFiscalLegivel { get; private set; }

    /// <summary>
    /// Número da nota fiscal extraído pela classificação de visão
    /// computacional (decisão do PO, incremento de garantia). Fato bruto —
    /// o indicador de garantia (<see cref="EWarrantyEstimateStatus"/>)
    /// nunca é persistido, sempre calculado na leitura a partir de
    /// <see cref="DataEmissaoNotaFiscal"/>.
    /// </summary>
    public string? NumeroNotaFiscal { get; private set; }

    /// <summary>Série da nota fiscal — mesma origem e mesma ressalva de <see cref="NumeroNotaFiscal"/>.</summary>
    public string? SerieNotaFiscal { get; private set; }

    /// <summary>
    /// Data de emissão da nota fiscal — dado principal para o indicador de
    /// garantia (hoje: prazo padrão global e provisório, configurável em
    /// <c>TriagemLlm:GarantiaPadraoMeses</c>; no futuro, prazo por produto e
    /// por empresa). Por isso só a data é persistida — o status calculado
    /// nunca é gravado.
    /// </summary>
    public DateTimeOffset? DataEmissaoNotaFiscal { get; private set; }

    public string? EtiquetaModelo { get; private set; }

    public string? EtiquetaSerie { get; private set; }

    public ETriagemMidiaDefeitoTipo? MidiaDefeitoTipo { get; private set; }

    public string? EnderecoCompleto { get; private set; }

    /// <summary>Opcional — pode ser coletado pelo operador após a assunção; não bloqueia a suficiência.</summary>
    public string? PontoReferencia { get; private set; }

    /// <summary>Opcional — pode ser coletado pelo operador após a assunção; não bloqueia a suficiência.</summary>
    public string? Localizacao { get; private set; }

    /// <summary>Desvios fora de contexto consecutivos. Zera a cada interação válida da triagem.</summary>
    public int DesviosConsecutivos { get; private set; }

    public IReadOnlyDictionary<ETriagemChecklistItem, int> TentativasInvalidasPorItem => _tentativasInvalidasPorItem;

    /// <summary>
    /// Checklist mínimo rígido completo: descrição, nota fiscal legível,
    /// etiqueta legível (modelo e série) e endereço completo. Foto/vídeo do
    /// defeito é opcional (decisão do PO — não bloqueia a suficiência, mesmo
    /// princípio já aplicado a <see cref="PontoReferencia"/> e
    /// <see cref="Localizacao"/>) e não entra no cálculo.
    /// </summary>
    public bool ChecklistMinimoCompleto =>
        !string.IsNullOrWhiteSpace(DescricaoDefeito)
        && NotaFiscalLegivel
        && !string.IsNullOrWhiteSpace(EtiquetaModelo)
        && !string.IsNullOrWhiteSpace(EtiquetaSerie)
        && !string.IsNullOrWhiteSpace(EnderecoCompleto);

    /// <summary>Quantidade total de itens do checklist mínimo rígido (épico, pool, coluna "itens completos/total").</summary>
    public const int ChecklistItensMinimosTotal = 4;

    /// <summary>
    /// Quantidade de itens do checklist mínimo rígido já preenchidos — cada
    /// um dos quatro itens de <see cref="ChecklistMinimoCompleto"/> conta
    /// no máximo 1, independentemente de quantas tentativas/mensagens o
    /// preencheram.
    /// </summary>
    public int ChecklistItensMinimosCompletos =>
        (string.IsNullOrWhiteSpace(DescricaoDefeito) ? 0 : 1)
        + (NotaFiscalLegivel ? 1 : 0)
        + (!string.IsNullOrWhiteSpace(EtiquetaModelo) && !string.IsNullOrWhiteSpace(EtiquetaSerie) ? 1 : 0)
        + (!string.IsNullOrWhiteSpace(EnderecoCompleto) ? 1 : 0);

    // ------------------------------------------------------------------
    // Transições de estado
    // ------------------------------------------------------------------

    public void MarcarContatoIniciado(DateTimeOffset now)
    {
        ExigirEstado(ETriagemStatus.AguardandoEnvio, nameof(MarcarContatoIniciado));
        Status = ETriagemStatus.ContatoIniciado;
        Touch(now);
    }

    public void MarcarFalhaDeContato(DateTimeOffset now)
    {
        ExigirEstado(ETriagemStatus.AguardandoEnvio, nameof(MarcarFalhaDeContato));
        Status = ETriagemStatus.FalhaDeContato;
        ColetaEncerradaEmUtc ??= now;
        Touch(now);
    }

    public void IniciarColeta(DateTimeOffset now)
    {
        ExigirEstado(ETriagemStatus.ContatoIniciado, nameof(IniciarColeta));
        Status = ETriagemStatus.EmColeta;
        Touch(now);
    }

    /// <summary>
    /// Expira a triagem por prazo (48h sem checklist mínimo). Permitida a
    /// partir de <see cref="ETriagemStatus.ContatoIniciado"/> ou
    /// <see cref="ETriagemStatus.EmColeta"/> (épico, seção 5).
    /// </summary>
    public void Expirar(DateTimeOffset now)
    {
        if (Status is not (ETriagemStatus.ContatoIniciado or ETriagemStatus.EmColeta))
        {
            throw new InvalidTriagemTransitionException(Status, nameof(Expirar));
        }

        Status = ETriagemStatus.Expirada;
        ColetaEncerradaEmUtc ??= now;
        Touch(now);
    }

    /// <summary>
    /// Marca o checklist como suficiente. Exige <see cref="ChecklistMinimoCompleto"/>
    /// — sem isso a operação falha e o estado não muda (pergunta 10 resolvida
    /// em 2026-10-13).
    /// </summary>
    public void MarcarInformacoesSuficientes(DateTimeOffset now)
    {
        ExigirEstado(ETriagemStatus.EmColeta, nameof(MarcarInformacoesSuficientes));

        if (!ChecklistMinimoCompleto)
        {
            throw new InvalidTriagemTransitionException(Status, nameof(MarcarInformacoesSuficientes));
        }

        Status = ETriagemStatus.InformacoesSuficientes;
        Touch(now);
    }

    public void LiberarAoPool(DateTimeOffset now)
    {
        ExigirEstado(ETriagemStatus.InformacoesSuficientes, nameof(LiberarAoPool));
        Status = ETriagemStatus.LiberadaAoPool;
        ColetaEncerradaEmUtc ??= now;
        Touch(now);
    }

    /// <summary>
    /// Encaminha a triagem para intervenção humana diretamente a partir de
    /// <see cref="ETriagemStatus.EmColeta"/> — usada pela condução da
    /// conversa pela LLM (plano M2,
    /// docs/implementation/PLANO-triagem-llm-jev.md §3/§4) quando o
    /// orçamento de chamadas da triagem é excedido ou quando o fornecedor da
    /// LLM está indisponível após as tentativas definidas
    /// (`triagem-llm-conducao.md` §4/§5). Diferente de
    /// <see cref="RegistrarTentativaInvalida"/> e <see cref="RegistrarDesvio"/>,
    /// não depende de um contador prévio — move imediatamente.
    /// </summary>
    public void MarcarAguardandoIntervencao(DateTimeOffset now)
    {
        ExigirEmColeta(nameof(MarcarAguardandoIntervencao));
        Status = ETriagemStatus.AguardandoIntervencao;
        ColetaEncerradaEmUtc ??= now;
        Touch(now);
    }

    /// <summary>
    /// Estados do pool compartilhado de operadores (épico, bloco C — pool de
    /// triagem). Fonte única usada tanto por <see cref="AssumirPorOperador"/>
    /// quanto pela listagem do pool, para que os dois nunca divirjam.
    /// </summary>
    public static readonly IReadOnlyCollection<ETriagemStatus> PoolStatuses =
    [
        ETriagemStatus.LiberadaAoPool,
        ETriagemStatus.Expirada,
        ETriagemStatus.FalhaDeContato,
        ETriagemStatus.AguardandoIntervencao,
    ];

    /// <summary>
    /// Estados "em triagem" (pré-pool): a LLM ainda conduz a coleta com o
    /// consumidor. Visíveis no Office apenas em modo leitura (gestão) — o
    /// Office não conversa com o consumidor nem assume a triagem.
    /// </summary>
    public static readonly IReadOnlyCollection<ETriagemStatus> InProgressStatuses =
    [
        ETriagemStatus.ContatoIniciado,
        ETriagemStatus.EmColeta,
        ETriagemStatus.InformacoesSuficientes,
    ];

    /// <summary>
    /// Um operador do posto assume a triagem com exclusividade (épico, "C3 —
    /// Assumir"). Permitida a partir de qualquer um dos <see cref="PoolStatuses"/>
    /// — não apenas <see cref="ETriagemStatus.LiberadaAoPool"/>, já que
    /// <see cref="ETriagemStatus.Expirada"/>, <see cref="ETriagemStatus.FalhaDeContato"/>
    /// e <see cref="ETriagemStatus.AguardandoIntervencao"/> também são
    /// encaminhadas ao pool para decisão humana. Estado terminal: a LLM para
    /// definitivamente neste atendimento (RF-TRIAGEM-005) e não há retorno
    /// ao pool na fase 1.
    /// </summary>
    public void AssumirPorOperador(Guid operadorUserId, DateTimeOffset now)
    {
        if (!PoolStatuses.Contains(Status))
        {
            throw new InvalidTriagemTransitionException(Status, nameof(AssumirPorOperador));
        }

        if (operadorUserId == Guid.Empty)
        {
            throw new ArgumentException("OperadorUserId não pode ser vazio.", nameof(operadorUserId));
        }

        Status = ETriagemStatus.AssumidaPorOperador;
        AssumidaPorUserId = operadorUserId;
        AssumidaEmUtc = now;
        Touch(now);
    }

    // ------------------------------------------------------------------
    // Registro dos itens do checklist — somente durante EmColeta.
    // Toda interação válida zera o contador de desvios consecutivos.
    // ------------------------------------------------------------------

    public void RegistrarDescricaoDefeito(string descricao, DateTimeOffset now)
    {
        ExigirEmColeta(nameof(RegistrarDescricaoDefeito));

        if (string.IsNullOrWhiteSpace(descricao))
        {
            throw new ArgumentException("Descrição do defeito não pode ser vazia.", nameof(descricao));
        }

        DescricaoDefeito = descricao.Trim();
        RegistrarInteracaoValida(now);
    }

    /// <param name="numero">Número da nota fiscal (opcional — quando a classificação de visão o extraiu com confiança).</param>
    /// <param name="serie">Série da nota fiscal (opcional, mesma origem de <paramref name="numero"/>).</param>
    /// <param name="dataEmissao">
    /// Data de emissão da nota fiscal (opcional, mesma origem). Dado
    /// principal para o indicador de garantia calculado na leitura — nunca
    /// o status em si (ver <see cref="DataEmissaoNotaFiscal"/>).
    /// </param>
    public void RegistrarNotaFiscalLegivel(DateTimeOffset now, string? numero = null, string? serie = null,
        DateTimeOffset? dataEmissao = null)
    {
        ExigirEmColeta(nameof(RegistrarNotaFiscalLegivel));
        NotaFiscalLegivel = true;

        if (!string.IsNullOrWhiteSpace(numero)) NumeroNotaFiscal = numero.Trim();
        if (!string.IsNullOrWhiteSpace(serie)) SerieNotaFiscal = serie.Trim();
        if (dataEmissao is not null) DataEmissaoNotaFiscal = dataEmissao;

        RegistrarInteracaoValida(now);
    }

    public void RegistrarEtiquetaLegivel(string modelo, string serie, DateTimeOffset now)
    {
        ExigirEmColeta(nameof(RegistrarEtiquetaLegivel));

        if (string.IsNullOrWhiteSpace(modelo))
        {
            throw new ArgumentException("Modelo da etiqueta não pode ser vazio.", nameof(modelo));
        }

        if (string.IsNullOrWhiteSpace(serie))
        {
            throw new ArgumentException("Série da etiqueta não pode ser vazia.", nameof(serie));
        }

        EtiquetaModelo = modelo.Trim();
        EtiquetaSerie = serie.Trim();
        RegistrarInteracaoValida(now);
    }

    /// <summary>Satisfeito por foto OU vídeo do defeito — qualquer um dos dois completa o item.</summary>
    public void RegistrarMidiaDefeito(ETriagemMidiaDefeitoTipo tipo, DateTimeOffset now)
    {
        ExigirEmColeta(nameof(RegistrarMidiaDefeito));
        MidiaDefeitoTipo = tipo;
        RegistrarInteracaoValida(now);
    }

    public void RegistrarEndereco(string enderecoCompleto, DateTimeOffset now)
    {
        ExigirEmColeta(nameof(RegistrarEndereco));

        if (string.IsNullOrWhiteSpace(enderecoCompleto))
        {
            throw new ArgumentException("Endereço completo não pode ser vazio.", nameof(enderecoCompleto));
        }

        EnderecoCompleto = enderecoCompleto.Trim();
        RegistrarInteracaoValida(now);
    }

    /// <summary>Opcional — não bloqueia a suficiência do checklist.</summary>
    public void RegistrarPontoReferencia(string? pontoReferencia, DateTimeOffset now)
    {
        ExigirEmColeta(nameof(RegistrarPontoReferencia));
        PontoReferencia = string.IsNullOrWhiteSpace(pontoReferencia) ? null : pontoReferencia.Trim();
        RegistrarInteracaoValida(now);
    }

    /// <summary>Opcional — não bloqueia a suficiência do checklist.</summary>
    public void RegistrarLocalizacao(string? localizacao, DateTimeOffset now)
    {
        ExigirEmColeta(nameof(RegistrarLocalizacao));
        Localizacao = string.IsNullOrWhiteSpace(localizacao) ? null : localizacao.Trim();
        RegistrarInteracaoValida(now);
    }

    // ------------------------------------------------------------------
    // Tentativas inválidas de mídia/item e desvios de contexto
    // ------------------------------------------------------------------

    /// <summary>
    /// Registra uma tentativa inválida (ex.: mídia ilegível) de um item do
    /// checklist. Contadores são por item e não se somam entre itens
    /// distintos. Ao atingir <see cref="MaxTentativasInvalidasPorItem"/> para
    /// o mesmo item, a triagem transita para
    /// <see cref="ETriagemStatus.AguardandoIntervencao"/>.
    /// </summary>
    public void RegistrarTentativaInvalida(ETriagemChecklistItem item, DateTimeOffset now)
    {
        ExigirEmColeta(nameof(RegistrarTentativaInvalida));

        _tentativasInvalidasPorItem.TryGetValue(item, out var tentativasAtuais);
        tentativasAtuais++;
        _tentativasInvalidasPorItem[item] = tentativasAtuais;
        Touch(now);

        if (tentativasAtuais >= MaxTentativasInvalidasPorItem)
        {
            Status = ETriagemStatus.AguardandoIntervencao;
            ColetaEncerradaEmUtc ??= now;
        }
    }

    /// <summary>
    /// Registra um desvio de contexto (mensagem fora do fluxo da triagem).
    /// Ao atingir <see cref="MaxDesviosConsecutivos"/> consecutivos, a
    /// triagem transita para <see cref="ETriagemStatus.AguardandoIntervencao"/>.
    /// </summary>
    public void RegistrarDesvio(DateTimeOffset now)
    {
        ExigirEmColeta(nameof(RegistrarDesvio));

        DesviosConsecutivos++;
        Touch(now);

        if (DesviosConsecutivos >= MaxDesviosConsecutivos)
        {
            Status = ETriagemStatus.AguardandoIntervencao;
            ColetaEncerradaEmUtc ??= now;
        }
    }

    private void RegistrarInteracaoValida(DateTimeOffset now)
    {
        DesviosConsecutivos = 0;
        Touch(now);
    }

    private void ExigirEmColeta(string operacao) => ExigirEstado(ETriagemStatus.EmColeta, operacao);

    private void ExigirEstado(ETriagemStatus esperado, string operacao)
    {
        if (Status != esperado)
        {
            throw new InvalidTriagemTransitionException(Status, operacao);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }
}
