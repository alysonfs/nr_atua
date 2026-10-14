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

    // ------------------------------------------------------------------
    // Checklist mínimo (épico, pergunta 10 resolvida em 2026-10-13).
    // Somente metadados são armazenados — nenhum binário de mídia.
    // ------------------------------------------------------------------

    public string? DescricaoDefeito { get; private set; }

    public bool NotaFiscalLegivel { get; private set; }

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
    /// etiqueta legível (modelo e série), ao menos uma foto OU vídeo do
    /// defeito, e endereço completo. Itens opcionais não entram no cálculo.
    /// </summary>
    public bool ChecklistMinimoCompleto =>
        !string.IsNullOrWhiteSpace(DescricaoDefeito)
        && NotaFiscalLegivel
        && !string.IsNullOrWhiteSpace(EtiquetaModelo)
        && !string.IsNullOrWhiteSpace(EtiquetaSerie)
        && MidiaDefeitoTipo is not null
        && !string.IsNullOrWhiteSpace(EnderecoCompleto);

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
        Touch(now);
    }

    public void AssumirPorOperador(DateTimeOffset now)
    {
        ExigirEstado(ETriagemStatus.LiberadaAoPool, nameof(AssumirPorOperador));
        Status = ETriagemStatus.AssumidaPorOperador;
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

    public void RegistrarNotaFiscalLegivel(DateTimeOffset now)
    {
        ExigirEmColeta(nameof(RegistrarNotaFiscalLegivel));
        NotaFiscalLegivel = true;
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

    private void Touch(DateTimeOffset now) => UpdatedAtUtc = now;
}
