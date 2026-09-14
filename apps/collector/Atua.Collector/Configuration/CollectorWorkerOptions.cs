namespace Atua.Collector.Configuration;

public sealed class CollectorWorkerOptions
{
    public const string SectionName = "CollectorWorker";

    /// <summary>URL base da API Atua (ex: https://api.atua.internal).</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Token opaco de ServiceCredential com escopo <c>collector.command.claim</c>.
    /// Configure via variável de ambiente ou secrets — nunca hardcode.
    /// </summary>
    public string ClaimServiceToken { get; set; } = string.Empty;

    /// <summary>
    /// Token opaco de ServiceCredential com escopo <c>collector.command.complete</c>.
    /// Configure via variável de ambiente ou secrets — nunca hardcode.
    /// </summary>
    public string CompleteServiceToken { get; set; } = string.Empty;

    /// <summary>
    /// Token opaco de ServiceCredential com escopo <c>collector.eligibility.read</c>.
    /// Configure via variável de ambiente ou secrets — nunca hardcode.
    /// </summary>
    public string EligibilityServiceToken { get; set; } = string.Empty;

    /// <summary>Intervalo de espera (segundos) quando o claim retorna 204 (sem comando disponível).</summary>
    public int PollingIntervalSeconds { get; set; } = 30;

    /// <summary>Executar o browser Playwright em modo headless.</summary>
    public bool Headless { get; set; } = true;

    /// <summary>
    /// Timeout base (ms) para operações de navegação/espera de seletor do
    /// Playwright contra o iService real. Descoberto empiricamente que o
    /// servidor Midea pode levar mais de 15s para responder a um simples GET
    /// a partir da região sa-east-1 (latência real do provedor, não é bug
    /// nem bloqueio de rede — confirmado via TLS handshake completo). Demais
    /// timeouts do fluxo de login/navegação escalam a partir deste valor.
    /// </summary>
    public int PageTimeoutMs { get; set; } = 60_000;

    /// <summary>
    /// Habilita o log de diagnóstico local (arquivo .txt) com o detalhe completo
    /// do ciclo de coleta (login CAS, requests/responses do iService, chamadas
    /// ClaimAsync/CompleteAsync à API Atua). Destinado exclusivamente ao uso local
    /// dos desenvolvedores para investigar o comportamento do iService — NUNCA
    /// deve ser habilitado em produção/AWS (mantenha <c>false</c> em
    /// <c>appsettings.json</c>; habilite apenas em <c>appsettings.Development.json</c>).
    /// Quando <c>false</c> (padrão), o logger de diagnóstico é no-op: nenhuma
    /// escrita em disco ocorre.
    /// </summary>
    public bool EnableLocalDebugLog { get; set; } = false;

    /// <summary>
    /// Diretório onde os arquivos de log de diagnóstico local são escritos, quando
    /// <see cref="EnableLocalDebugLog"/> está habilitado. Caminho relativo ao
    /// working directory do processo. O diretório é criado automaticamente se
    /// não existir.
    /// </summary>
    public string LocalDebugLogDirectory { get; set; } = "./logs/iservice-debug";

    /// <summary>
    /// TTL (horas) aplicado a <c>expires_at</c> de <c>provider_sessions</c> a cada login CAS
    /// bem-sucedido (RF-023.3/DP-023.1). Valor conservador inicial sem dado empírico da
    /// duração real de uma sessão CAS aceita pela Midea — ajustar por observação em
    /// produção; não é decisão arquitetural, apenas parametrização operacional.
    /// </summary>
    public int SessionTtlHours { get; set; } = 4;

    /// <summary>
    /// Estratégia de busca de OS no iService (RF-024/Fase 3 do refactor de
    /// <c>provider_interactions</c>). Quando <c>true</c> (padrão), busca todas as OS do
    /// período em uma única chamada paginada a <c>queryWorkOrder</c> com
    /// <c>woStatus=""</c>/<c>woStatusCond="me"</c> e <c>creationDateFrom</c>/
    /// <c>creationDateTo</c> derivados de <c>HistoryWindowMonths</c>, agrupando o
    /// resultado por <c>woStatus</c>. Quando <c>false</c>, mantém a estratégia legada de
    /// 1 chamada paginada por status (5 chamadas fixas). Flag de rollback rápido — sem
    /// redeploy — caso a busca por data se comporte de forma inesperada contra o
    /// iService real; remover assim que a Fase 3 estiver validada em produção.
    /// </summary>
    public bool UseDateBasedWorkOrderQuery { get; set; } = true;
}
