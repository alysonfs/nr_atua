using System.Text.Json;
using Atua.Collector.Configuration;
using Atua.Collector.Diagnostics;
using Atua.Collector.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Atua.Collector.IService;

/// <summary>
/// Implementação do coletor de OS do portal iService usando Microsoft.Playwright.
/// Porta a lógica da POC Node/Puppeteer em
/// <c>rag/iservice/automated-login-ics-amer-robot/robots/ics-amer/src/robot.js</c>.
///
/// Simplificações desta etapa (TODO para próximas iterações):
/// <list type="bullet">
///   <item>Sessão não é persistida — login é realizado a cada execução.</item>
/// </list>
///
/// OS coletadas são convertidas de <see cref="JsonElement"/> para um grafo de objetos
/// nativo (<see cref="Dictionary{TKey,TValue}"/>/<see cref="List{T}"/>/primitivos) via
/// <see cref="ConvertJsonElement"/> antes de retornar em <see cref="CollectionResult"/>,
/// para que os adapters do consumer (<see cref="Atua.Collector.Consumer.IProviderInteractionOrderAdapter"/>)
/// consigam reconhecê-las como dicionário.
/// </summary>
public sealed class IServiceCollectorService(
    IOptions<CollectorWorkerOptions> options,
    ILogger<IServiceCollectorService> logger,
    IIServiceDebugLogger debugLogger,
    IProviderInteractionRepository providerInteractionRepository,
    IProviderSessionRepository providerSessionRepository)
    : IIServiceCollector
{
    private const string LoginUrl = "https://signin.midea.com/login?service=https://ics-amer.midea.com/";
    private const string IServiceHost = "ics-amer.midea.com";
    private const string SigninHost = "signin.midea.com";
    private const string WoListUrl = "https://ics-amer.midea.com/web/iservice-wom/workOrder/queryWorkOrder";
    private const string WoDetailUrl = "https://ics-amer.midea.com/web/iservice-wom/workOrder/queryOneWorkOrder";
    // ADR-021 (D1+D6) sugeria 50 como valor inicial de WORKER_PAGE_SIZE; estava
    // hardcoded em 200. Reduzido após ciclo real (3 meses, 200/página) ter
    // devolvido 1035 OS em 6 páginas e o consumer da Fase 4 não ter conseguido
    // processar todas as páginas do ciclo a tempo (ver validação de Fase 4).
    private const int StatusPageSize = 50;
    private const string ProviderTypeName = "iservice";

    private static readonly (string Key, string Code, string TabLabel)[] StatusConfigs =
    [
        ("assigned", "assigned", "Designado"),
        ("accepted", "accepted", "Em Processamento"),
        ("pending", "pending", "Pendente"),
        ("closed", "closed", "Concluído"),
        ("cancelled", "cancelled", "Cancelado"),
    ];

    private readonly CollectorWorkerOptions _options = options.Value;

    // Timeouts escalam a partir de PageTimeoutMs (ver CollectorWorkerOptions):
    // o servidor Midea real observado é significativamente mais lento do que
    // os valores fixos originais (15-30s) previam.
    private int PageTimeout => _options.PageTimeoutMs;
    private int HalfPageTimeout => _options.PageTimeoutMs / 2;

    /// <inheritdoc/>
    public async Task<CollectionResult> CollectAsync(
        Guid tenantId,
        Guid commandId,
        string username,
        string password,
        string? baseUrl,
        int historyWindowMonths,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[COLETOR] Iniciando coleta para usuário={Username}.", username);

        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _options.Headless,
            Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"],
        });

        try
        {
            return await ExecuteCollectionCycleAsync(
                browser, tenantId, commandId, username, password, historyWindowMonths, forceLogin: false, cancellationToken);
        }
        catch (ProviderSessionInvalidException ex)
        {
            // RF-023.4/RN-023.4: sessão inválida detectada em pleno ciclo (HTTP 401 em
            // list_query/detail_query). Refaz login CAS uma única vez dentro do mesmo
            // ciclo antes de desistir e propagar a falha do comando (critério de aceite 4).
            logger.LogWarning(
                "[SESSAO] Sessão invalidada em pleno ciclo ({Reason}). Refazendo login (tentativa única).",
                ex.Message);
            return await ExecuteCollectionCycleAsync(
                browser, tenantId, commandId, username, password, historyWindowMonths, forceLogin: true, cancellationToken);
        }
    }

    /// <summary>
    /// Executa um ciclo completo de coleta em um <see cref="IBrowserContext"/> novo.
    /// Quando <paramref name="forceLogin"/> é falso, tenta reaproveitar a sessão salva em
    /// <c>provider_sessions</c> (RF-023.1/RF-023.2) hidratando o contexto via
    /// <c>storage_state</c> e validando com uma navegação barata; se não houver sessão
    /// válida, faz login CAS completo e persiste a nova sessão (RF-023.3). Uma
    /// <see cref="ProviderSessionInvalidException"/> lançada por qualquer chamada ao
    /// provedor durante o ciclo (401 em pleno voo) propaga para <see cref="CollectAsync"/>
    /// acionar o retry único com <c>forceLogin: true</c>.
    /// </summary>
    private async Task<CollectionResult> ExecuteCollectionCycleAsync(
        IBrowser browser,
        Guid tenantId,
        Guid commandId,
        string username,
        string password,
        int historyWindowMonths,
        bool forceLogin,
        CancellationToken cancellationToken)
    {
        var savedStorageState = forceLogin
            ? null
            : await GetValidSessionSafeAsync(tenantId, cancellationToken);

        var contextOptions = new BrowserNewContextOptions
        {
            // O portal iService detecta idioma via Accept-Language/navigator.language
            // e mostra o formulário de login em inglês por padrão em ambientes sem
            // locale explícito (confirmado via captura de tela em produção: o
            // seletor original da POC, baseado em placeholder="Conta"/"Senha", só
            // funciona com a página em Português). Forçamos pt-BR para casar com o
            // comportamento observado na POC original.
            Locale = "pt-BR",
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "pt-BR,pt;q=0.9",
            },
        };
        if (savedStorageState is not null)
        {
            contextOptions.StorageState = savedStorageState;
        }

        await using var context = await browser.NewContextAsync(contextOptions);
        var page = await context.NewPageAsync();

        // Guard somente-leitura: bloqueia POSTs de escrita no iService
        await SetupReadOnlyGuardAsync(page);

        // Log de diagnóstico local (no-op quando desabilitado): observa passivamente,
        // via evento de rede do Playwright, todas as respostas de queryWorkOrder/
        // queryOneWorkOrder — sem alterar em nada a lógica de negócio dos fetch()
        // disparados via page.EvaluateAsync.
        if (debugLogger.Enabled)
        {
            page.Response += (_, response) => _ = LogIServiceResponseSafeAsync(commandId, response);
        }

        try
        {
            var usedSavedSession = false;

            if (savedStorageState is not null)
            {
                usedSavedSession = await TryHydrateSavedSessionAsync(page, cancellationToken);
                if (!usedSavedSession)
                {
                    await InvalidateSessionSafeAsync(tenantId, cancellationToken);
                }
            }

            if (usedSavedSession)
            {
                logger.LogInformation("[SESSAO] Sessão reaproveitada — login CAS não executado neste ciclo.");
            }
            else
            {
                await ExecuteLoginWithInteractionLogAsync(tenantId, commandId, page, username, password, cancellationToken);

                logger.LogInformation("[COLETOR] Abrindo visão por status de OS...");
                await page.GotoAsync($"https://{IServiceHost}/",
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = PageTimeout });

                await PersistSessionSafeAsync(tenantId, context, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var requestTemplate = await OpenWorkOrderStatusViewAsync(page);
            debugLogger.LogTemplateCaptured(commandId, requestTemplate.Url, requestTemplate.Headers, requestTemplate.Body);

            cancellationToken.ThrowIfCancellationRequested();

            Dictionary<string, IReadOnlyList<object>> ordersByStatus;

            if (_options.UseDateBasedWorkOrderQuery)
            {
                // Fase 3 (RF-024): 1 chamada paginada cobrindo todo o período, em vez de
                // 5 chamadas fixas (uma por status). O próprio iService devolve o status
                // de cada OS no payload — agrupamos depois, não filtramos na origem.
                var (creationDateFrom, creationDateTo) = ComputeCreationDateRange(historyWindowMonths, DateTime.UtcNow);
                logger.LogInformation(
                    "[COLETOR] Iniciando coleta por data (período {From} a {To}, todos os status em 1 fluxo paginado)...",
                    creationDateFrom, creationDateTo);
                ordersByStatus = await FetchWorkOrdersByDateRangeAsync(
                    tenantId, commandId, page, requestTemplate, creationDateFrom, creationDateTo, cancellationToken);
            }
            else
            {
                logger.LogInformation("[COLETOR] Iniciando coleta por status (estratégia legada)...");
                ordersByStatus = new Dictionary<string, IReadOnlyList<object>>();
                foreach (var (key, code, tabLabel) in StatusConfigs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var orders = await FetchWorkOrdersForStatusAsync(tenantId, commandId, page, requestTemplate, code, tabLabel, cancellationToken);
                    ordersByStatus[key] = orders;
                }
            }

            var statusCounts = ordersByStatus.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
            foreach (var (key, _, tabLabel) in StatusConfigs)
            {
                logger.LogInformation("[COLETA] {Count} OS '{TabLabel}' coletadas.", statusCounts.GetValueOrDefault(key), tabLabel);
            }

            // Enriquecimento: apenas OS "assigned" recebem detalhe
            var assignedOrders = await EnrichAssignedOrdersAsync(tenantId, commandId, page, requestTemplate, ordersByStatus["assigned"], cancellationToken);
            ordersByStatus["assigned"] = assignedOrders;

            logger.LogInformation(
                "[RESULTADO] Designado={Assigned} | Em Processamento={Accepted} | " +
                "Pendente={Pending} | Concluído={Closed} | Cancelado={Cancelled}",
                statusCounts["assigned"],
                statusCounts["accepted"],
                statusCounts["pending"],
                statusCounts["closed"],
                statusCounts["cancelled"]);

            debugLogger.LogCollectResult(commandId, statusCounts, success: true, exceptionType: null, exceptionMessage: null);

            return new CollectionResult(
                DateTimeOffset.UtcNow,
                statusCounts,
                ordersByStatus);
        }
        catch (ProviderSessionInvalidException)
        {
            // A sessão pode ter sido reaproveitada (usedSavedSession) ou obtida por login
            // normal neste mesmo ciclo — de qualquer forma, uma vez rejeitada em pleno voo
            // ela não deve mais ser oferecida ao próximo ciclo/retry.
            await InvalidateSessionSafeAsync(tenantId, cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            debugLogger.LogCollectResult(commandId, null, success: false, ex.GetType().Name, ex.Message);
            throw;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Persistência de sessão (RF-023)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Busca <c>storage_state</c> salvo e ainda válido (RF-023.1). Absorve qualquer
    /// exceção de infraestrutura (Mongo indisponível etc.) — falha aqui degrada
    /// graciosamente para login CAS normal, nunca derruba o ciclo de coleta.
    /// </summary>
    private async Task<string?> GetValidSessionSafeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            return await providerSessionRepository.GetValidStorageStateAsync(tenantId, ProviderTypeName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SESSAO] Falha ao buscar sessão salva. Prosseguindo com login CAS normal.");
            return null;
        }
    }

    /// <summary>
    /// Validação barata (RF-023.2/RN-023.2) de uma sessão hidratada a partir de
    /// <c>storage_state</c> salvo: navega para a home do iService e confirma que não houve
    /// redirect para a tela de login CAS (<see cref="SigninHost"/>). Nunca lança em caso de
    /// sessão inválida — apenas retorna <c>false</c>, deixando o chamador decidir (login CAS
    /// normal). ATENÇÃO (RF-023.6): não deve logar <c>storage_state</c> em nenhuma hipótese.
    /// </summary>
    private async Task<bool> TryHydrateSavedSessionAsync(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            await page.GotoAsync($"https://{IServiceHost}/",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = PageTimeout });

            cancellationToken.ThrowIfCancellationRequested();

            var currentHost = new Uri(page.Url).Host;
            if (string.Equals(currentHost, SigninHost, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("[SESSAO] Sessão salva rejeitada pelo provedor (redirect para login). Refazendo login.");
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SESSAO] Falha ao validar sessão salva. Tratando como inválida e refazendo login.");
            return false;
        }
    }

    /// <summary>
    /// Persiste <c>storage_state</c> do contexto atual logo após um login CAS bem-sucedido
    /// (RF-023.3), com TTL configurável (<see cref="CollectorWorkerOptions.SessionTtlHours"/>,
    /// DP-023.1). Absorve qualquer exceção — falha ao persistir apenas significa que o
    /// próximo ciclo fará login novamente, não é falha da coleta atual.
    /// </summary>
    private async Task PersistSessionSafeAsync(Guid tenantId, IBrowserContext context, CancellationToken cancellationToken)
    {
        try
        {
            var storageState = await context.StorageStateAsync();
            await providerSessionRepository.SaveValidSessionAsync(
                tenantId, ProviderTypeName, storageState, TimeSpan.FromHours(_options.SessionTtlHours), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SESSAO] Falha ao persistir sessão após login bem-sucedido. Próximo ciclo fará login novamente.");
        }
    }

    /// <summary>
    /// Marca a sessão salva como inválida (RF-023.4). Absorve qualquer exceção — mesma
    /// postura defensiva das demais operações de sessão.
    /// </summary>
    private async Task InvalidateSessionSafeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await providerSessionRepository.InvalidateSessionAsync(tenantId, ProviderTypeName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SESSAO] Falha ao invalidar sessão.");
        }
    }

    /// <summary>
    /// Sinaliza que a sessão do provedor foi rejeitada (HTTP 401) durante uma chamada
    /// autenticada em pleno ciclo de coleta (RF-023.4/RN-023.4, critério de aceite 4).
    /// Capturada por <see cref="ExecuteCollectionCycleAsync"/>/<see cref="CollectAsync"/>
    /// para acionar invalidação da sessão + login CAS único antes de desistir.
    /// </summary>
    private sealed class ProviderSessionInvalidException(string reason) : Exception(reason);

    // -------------------------------------------------------------------------
    // Guard somente-leitura
    // -------------------------------------------------------------------------

    private async Task SetupReadOnlyGuardAsync(IPage page)
    {
        await page.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            if (IsWriteRequestOnIService(request.Method, request.Url))
            {
                logger.LogWarning("[READONLY] Bloqueado: {Method} {Url}", request.Method, request.Url);
                await route.AbortAsync("blockedbyclient");
                return;
            }
            await route.ContinueAsync();
        });

        logger.LogInformation("[READONLY] Modo somente leitura ativo para iService.");
    }

    /// <summary>
    /// Classifica se uma requisição é de escrita no iService (RF-013). Extraído como
    /// método <c>public static</c> — sem dependência de Playwright — para permitir teste
    /// unitário isolado (DP-013.2).
    /// </summary>
    public static bool IsWriteRequestOnIService(string method, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Host, IServiceHost, StringComparison.OrdinalIgnoreCase)) return false;

        const string IServiceWomPrefix = "/web/iservice-wom/";
        if (!uri.AbsolutePath.StartsWith(IServiceWomPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Fora de /web/iservice-wom/ (módulo de negócio de OS): o mesmo host serve
            // infraestrutura do portal alheia a dados de OS/cliente — sessão CAS
            // (/web/auth-server/...) e telemetria de erro do cliente
            // (/web/iservice-admin/htmlAppErrorLog/insertLog), confirmados em produção
            // (2026-09-02) como necessários ao login/navegação normais. Bloquear por
            // padrão aqui quebra o Coletor sem ganho real de proteção — não são ações
            // de escrita sobre dados de OS. Não bloqueado.
            return false;
        }

        // Dentro de /web/iservice-wom/ (todos os submódulos de negócio de OS — não
        // apenas /workOrder/): postura default-deny (DP-013.1). GET/HEAD/OPTIONS
        // nunca são bloqueados; PUT/PATCH/DELETE são sempre escrita; POST só passa
        // se o endpoint indicar consulta.
        if (IsSafeReadMethod(method)) return false;

        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            // PUT/PATCH/DELETE dentro de /web/iservice-wom/: sempre escrita.
            return true;
        }

        if (IsKnownReadOnlyDashboardEndpoint(uri.AbsolutePath))
        {
            // Contadores/indicadores da home do iService (ex.: desktop/indicator/assigned,
            // holiday/list), descobertos ao vivo em produção (2026-09-02): são chamados
            // via POST pela SPA ao renderizar a "Visão por Status", mas são consultas
            // agregadas somente leitura, sem efeito sobre dados de OS. Bloqueá-los quebra
            // a navegação do Coletor (a SPA entra em estado de erro e destrói o contexto
            // de execução) sem qualquer ganho de proteção real. Não bloqueado.
            return false;
        }

        var endpoint = uri.AbsolutePath.Split('/').LastOrDefault() ?? string.Empty;
        return !endpoint.StartsWith("query", StringComparison.OrdinalIgnoreCase)
            && !endpoint.StartsWith("get", StringComparison.OrdinalIgnoreCase)
            && !endpoint.StartsWith("select", StringComparison.OrdinalIgnoreCase)
            && !endpoint.StartsWith("list", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Endpoints de contadores/indicadores do painel da home do iService (submódulo
    /// <c>desktop/indicator/</c>) e a lista de feriados (<c>holiday/list</c>) usada para
    /// cálculo de datas úteis. Confirmados como somente leitura ao vivo em produção
    /// (2026-09-02): a SPA os chama ao abrir a "Visão por Status", antes de qualquer
    /// consulta real de OS.
    /// </summary>
    private static bool IsKnownReadOnlyDashboardEndpoint(string absolutePath) =>
        absolutePath.Contains("/desktop/indicator/", StringComparison.OrdinalIgnoreCase)
        || absolutePath.EndsWith("/holiday/list", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeReadMethod(string method) =>
        method.Equals("GET", StringComparison.OrdinalIgnoreCase)
        || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
        || method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // Login CAS
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executa o login CAS e registra exatamente um documento em <c>provider_interactions</c>
    /// (RF-022.1/RN-022.1, critério de aceite 4) — sucesso ou falha, nunca as duas coisas.
    /// Credenciais nunca entram no documento (RF-022.7/RN-022.7): apenas o username, que já
    /// é logado em texto claro pelo Worker em outros pontos.
    /// </summary>
    private async Task ExecuteLoginWithInteractionLogAsync(
        Guid tenantId, Guid commandId, IPage page, string username, string password, CancellationToken cancellationToken)
    {
        var request = new Dictionary<string, object?>
        {
            ["username"] = username,
            ["login_url"] = LoginUrl,
        };

        try
        {
            await DoLoginAsync(commandId, page, username, password, cancellationToken);
            await LogProviderInteractionSafeAsync(
                tenantId, commandId, "login", request, orders: null, success: true, errorMessage: null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await LogProviderInteractionSafeAsync(
                tenantId, commandId, "login", request, orders: null, success: false, ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Registra uma interação em <c>provider_interactions</c> absorvendo qualquer exceção —
    /// falha ao auditar não pode se tornar uma nova causa de falha do ciclo de coleta
    /// (mesma postura defensiva do log de diagnóstico local).
    /// </summary>
    private async Task LogProviderInteractionSafeAsync(
        Guid tenantId,
        Guid commandId,
        string interactionType,
        IReadOnlyDictionary<string, object?> request,
        IReadOnlyList<object?>? orders,
        bool success,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await providerInteractionRepository.InsertInteractionAsync(
                tenantId, commandId, ProviderTypeName, interactionType, request, orders, success, errorMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[COLETOR] Falha ao registrar provider_interactions '{InteractionType}'. CommandId={CommandId}.",
                interactionType, commandId);
        }
    }

    private async Task DoLoginAsync(Guid commandId, IPage page, string username, string password, CancellationToken cancellationToken)
    {
        // NUNCA logar password
        logger.LogInformation("[LOGIN] Iniciando fluxo de login CAS para usuário={Username}.", username);
        debugLogger.LogLoginStep(commandId, "goto-login-page", LoginUrl, "iniciado");

        try
        {
            await page.GotoAsync(LoginUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = PageTimeout,
            });
        }
        catch (TimeoutException ex)
        {
            debugLogger.LogLoginStep(commandId, "goto-login-page", LoginUrl, "timeout", ex.Message);
            await CaptureDebugArtifactsAsync(page, "login-goto-timeout");
            throw new IServiceUnavailableEx("Timeout ao carregar página de login CAS.", ex);
        }

        debugLogger.LogLoginStep(commandId, "goto-login-page", LoginUrl, "sucesso");
        cancellationToken.ThrowIfCancellationRequested();

        const string usernameSelector = "input[placeholder='Conta']";
        const string passwordSelector = "input[placeholder='Senha']";

        try
        {
            await page.WaitForSelectorAsync(usernameSelector, new PageWaitForSelectorOptions { Timeout = PageTimeout });
        }
        catch (TimeoutException ex)
        {
            debugLogger.LogLoginStep(commandId, "wait-username-field", page.Url, "timeout", ex.Message);
            await CaptureDebugArtifactsAsync(page, "login-username-timeout");
            throw new IServiceUnavailableEx("Timeout aguardando campo de usuário na página de login CAS.", ex);
        }

        await page.ClickAsync(usernameSelector);
        await page.FillAsync(usernameSelector, username);

        await page.ClickAsync(passwordSelector);
        await page.FillAsync(passwordSelector, password);

        logger.LogInformation("[LOGIN] Submetendo formulário...");
        debugLogger.LogLoginStep(commandId, "submit-form", page.Url, "submetido", $"Username={username}");
        await page.ClickAsync("button");

        cancellationToken.ThrowIfCancellationRequested();

        // Aguarda redirecionamento para o iService
        try
        {
            await page.WaitForFunctionAsync(
                $"() => window.location.hostname === '{IServiceHost}'",
                null,
                new PageWaitForFunctionOptions { Timeout = PageTimeout });
        }
        catch (TimeoutException)
        {
            // Verifica se ainda está no signin (credencial rejeitada)
            var currentUrl = page.Url;
            if (currentUrl.Contains(SigninHost))
            {
                logger.LogWarning("[LOGIN] Credencial rejeitada para usuário={Username}.", username);
                debugLogger.LogLoginStep(commandId, "wait-redirect", currentUrl, "falha", "Credencial rejeitada (ainda em signin)");
                throw new CredentialRejectedEx($"Login CAS rejeitado para o usuário '{username}'.");
            }

            debugLogger.LogLoginStep(commandId, "wait-redirect", currentUrl, "timeout");
            throw new IServiceUnavailableEx("Timeout aguardando redirecionamento pós-login para o iService.");
        }

        logger.LogInformation("[LOGIN] Autenticado com sucesso no iService. URL={Url}", page.Url);
        debugLogger.LogLoginStep(commandId, "wait-redirect", page.Url, "sucesso", $"URL final={page.Url}");
    }

    /// <summary>
    /// Captura screenshot + HTML da página em <c>/tmp</c> para diagnóstico manual
    /// quando um timeout inesperado ocorre. Débito técnico temporário — não deve
    /// permanecer em produção além da fase de estabilização do login real.
    /// </summary>
    private async Task CaptureDebugArtifactsAsync(IPage page, string tag)
    {
        try
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var basePath = Path.Combine(Path.GetTempPath(), $"iservice-debug-{tag}-{stamp}");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = $"{basePath}.png", FullPage = true });
            var html = await page.ContentAsync();
            await File.WriteAllTextAsync($"{basePath}.html", html);
            logger.LogWarning(
                "[DEBUG] Artefatos de diagnóstico salvos em {BasePath}.png/.html. URL={Url}",
                basePath, page.Url);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DEBUG] Falha ao capturar artefatos de diagnóstico.");
        }
    }

    // -------------------------------------------------------------------------
    // Log de diagnóstico local — captura passiva de respostas de rede
    // -------------------------------------------------------------------------

    /// <summary>
    /// Observa (via evento de rede do Playwright, sem interferir na lógica de negócio
    /// dos fetch() executados dentro de <c>page.EvaluateAsync</c>) as respostas de
    /// <c>queryWorkOrder</c>/<c>queryOneWorkOrder</c> e as escreve no log de diagnóstico
    /// local. Nunca deve lançar exceção — qualquer falha é apenas registrada via
    /// <see cref="ILogger"/> como warning e descartada.
    /// </summary>
    private async Task LogIServiceResponseSafeAsync(Guid commandId, IResponse response)
    {
        try
        {
            var url = response.Url;
            var isWorkOrderList = url.Contains("queryWorkOrder", StringComparison.OrdinalIgnoreCase);
            var isWorkOrderDetail = url.Contains("queryOneWorkOrder", StringComparison.OrdinalIgnoreCase);
            if (!isWorkOrderList && !isWorkOrderDetail) return;

            int? statusCode = null;
            try { statusCode = response.Status; } catch { /* resposta pode já ter sido descartada */ }

            string? responseBody = null;
            try { responseBody = await response.TextAsync(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[DEBUGLOG] Falha ao ler corpo da resposta de {Url} para diagnóstico.", url);
            }

            var requestBody = response.Request.PostData;

            if (isWorkOrderDetail)
            {
                var workOrderId = TryExtractWorkOrderId(requestBody);
                debugLogger.LogEnrichmentExchange(commandId, url, workOrderId, statusCode, responseBody);
            }
            else
            {
                debugLogger.LogWorkOrderExchange(commandId, url, statusCode, requestBody, responseBody);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DEBUGLOG] Falha inesperada ao capturar resposta de diagnóstico do iService.");
        }
    }

    private static string? TryExtractWorkOrderId(string? requestBodyJson)
    {
        if (string.IsNullOrWhiteSpace(requestBodyJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(requestBodyJson);
            if (doc.RootElement.TryGetProperty("workOrderId", out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
        }
        catch (JsonException)
        {
            // corpo não é JSON válido — ignora, não é crítico para o log de diagnóstico.
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Navegação e captura do template
    // -------------------------------------------------------------------------

    private async Task<RequestTemplate> OpenWorkOrderStatusViewAsync(IPage page)
    {
        // Aguarda a home do iService carregar
        try
        {
            await page.WaitForFunctionAsync(
                "() => document.body.innerText.includes('Links Rápidos') && document.body.innerText.includes('Ordem de Serviço')",
                null,
                new PageWaitForFunctionOptions { Timeout = HalfPageTimeout });
        }
        catch (TimeoutException ex)
        {
            throw new IServiceUnavailableEx("Timeout aguardando a home do iService carregar.", ex);
        }

        await page.WaitForTimeoutAsync(1500);

        // Prepara captura do primeiro request queryWorkOrder (sem CountStatus)
        var templateTcs = new TaskCompletionSource<RequestTemplate>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(HalfPageTimeout));
        cts.Token.Register(() => templateTcs.TrySetException(
            new IServiceUnavailableEx("Timeout capturando template da API queryWorkOrder.")));

        page.Request += (_, req) =>
        {
            if (!req.Url.Contains("queryWorkOrder") || req.Url.Contains("CountStatus")) return;
            try
            {
                var body = string.IsNullOrWhiteSpace(req.PostData)
                    ? new Dictionary<string, object?>()
                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(req.PostData) ?? [];

                templateTcs.TrySetResult(new RequestTemplate(req.Url, req.Headers, body));
            }
            catch (Exception ex)
            {
                templateTcs.TrySetException(ex);
            }
        };

        // Clica em "Visão por Status" no menu
        var clicked = await page.EvaluateAsync<bool>(@"() => {
            const normalize = (v) =>
                String(v || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/\s+/g, ' ').trim().toLowerCase();
            const target = 'visao por status';
            const selectors = ['span.menu-title', 'li.el-menu-item', '.item-container'];
            for (const sel of selectors) {
                const match = Array.from(document.querySelectorAll(sel))
                    .find(n => normalize(n.textContent).includes(target));
                if (match) { match.click(); return true; }
            }
            return false;
        }");

        if (!clicked)
            throw new IServiceUnavailableEx("Não foi possível localizar o item de menu 'Visão por Status' na home do iService.");

        // Aguarda navegação para a tela de status
        try
        {
            await page.WaitForFunctionAsync(
                "() => window.location.hash.includes('/wom/views/serviceExecution/workOrderExecution/index')",
                null,
                new PageWaitForFunctionOptions { Timeout = HalfPageTimeout });

            await page.WaitForFunctionAsync(
                "() => document.body.innerText.includes('Designado') && document.body.innerText.includes('Em Processamento')",
                null,
                new PageWaitForFunctionOptions { Timeout = HalfPageTimeout });
        }
        catch (TimeoutException ex)
        {
            throw new IServiceUnavailableEx("Timeout aguardando a tela 'Visão por Status' carregar.", ex);
        }

        return await templateTcs.Task;
    }

    // -------------------------------------------------------------------------
    // Coleta de OS por data (Fase 3, RF-024) — estratégia atual
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calcula o intervalo <c>creationDateFrom</c>/<c>creationDateTo</c> (formato aceito
    /// pelo iService, <c>yyyy-MM-dd HH:mm:ss</c>) a partir de <c>historyWindowMonths</c>
    /// (RF-009, default 3 meses). Extraído como método <c>public static</c> — sem
    /// dependência de Playwright — para permitir teste unitário isolado (DP-013.2),
    /// mesmo padrão de <see cref="IsWriteRequestOnIService"/>.
    /// </summary>
    public static (string CreationDateFrom, string CreationDateTo) ComputeCreationDateRange(
        int historyWindowMonths, DateTime nowUtc)
    {
        var months = historyWindowMonths > 0 ? historyWindowMonths : 3;
        var fromDate = nowUtc.Date.AddMonths(-months);
        var toDate = nowUtc.Date;
        return (
            fromDate.ToString("yyyy-MM-dd") + " 00:00:00",
            toDate.ToString("yyyy-MM-dd") + " 23:59:59");
    }

    /// <summary>
    /// Busca todas as OS do período em uma única sequência paginada (RF-024), com
    /// <c>woStatus=""</c>/<c>woStatusCond="me"</c> — substitui as 5 chamadas fixas por
    /// status da estratégia legada (<see cref="FetchWorkOrdersForStatusAsync"/>). Agrupa o
    /// resultado por <c>woStatus</c> (campo devolvido pelo próprio iService em cada OS).
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyList<object>>> FetchWorkOrdersByDateRangeAsync(
        Guid tenantId,
        Guid commandId,
        IPage page,
        RequestTemplate template,
        string creationDateFrom,
        string creationDateTo,
        CancellationToken cancellationToken)
    {
        var allOrders = new List<object>();
        // Cobre até 200 * 200 = 40.000 OS no período — folga generosa sobre o volume
        // observado em produção (~220 OS/mês no tenant de teste Natal Refrigeração).
        const int maxPages = 200;

        for (var pageNumber = 1; pageNumber <= maxPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = await FetchWorkOrdersDateRangePageWithInteractionLogAsync(
                tenantId, commandId, page, template, creationDateFrom, creationDateTo, pageNumber, cancellationToken);
            allOrders.AddRange(chunk);
            if (chunk.Count < StatusPageSize) break;
        }

        return GroupOrdersByStatus(allOrders);
    }

    /// <summary>
    /// Envolve a página de busca por data registrando exatamente um documento em
    /// <c>provider_interactions</c> por chamada (RF-022.1/RN-022.1) — mesmo padrão de
    /// <see cref="FetchWorkOrdersPageWithInteractionLogAsync"/>.
    /// </summary>
    private async Task<IReadOnlyList<object>> FetchWorkOrdersDateRangePageWithInteractionLogAsync(
        Guid tenantId,
        Guid commandId,
        IPage page,
        RequestTemplate template,
        string creationDateFrom,
        string creationDateTo,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        var request = new Dictionary<string, object?>
        {
            ["url"] = template.Url,
            ["status"] = "",
            ["status_cond"] = "me",
            ["creation_date_from"] = creationDateFrom,
            ["creation_date_to"] = creationDateTo,
            ["page"] = pageNumber,
            ["page_size"] = StatusPageSize,
        };

        try
        {
            var orders = await FetchWorkOrdersPageAsync(
                page, template, statusCode: "", woStatusCond: "me",
                creationDateFrom: creationDateFrom, creationDateTo: creationDateTo, pageNumber: pageNumber);
            await LogProviderInteractionSafeAsync(
                tenantId, commandId, "list_query", request, orders, success: true, errorMessage: null, cancellationToken);
            return orders;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await LogProviderInteractionSafeAsync(
                tenantId, commandId, "list_query", request, orders: null, success: false, ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Agrupa OS já convertidas (<see cref="ConvertJsonElement"/>) por <c>woStatus</c>,
    /// garantindo que todas as chaves de <see cref="StatusConfigs"/> apareçam no resultado
    /// (mesmo com 0 OS), para manter o mesmo formato de <c>statusCounts</c>/
    /// <c>ordersByStatus</c> que a estratégia legada produzia. OS com <c>woStatus</c>
    /// desconhecido (fora de <see cref="StatusConfigs"/>) não são descartadas — apenas não
    /// entram em <c>statusCounts</c>; ficam registradas normalmente no payload bruto de
    /// <c>provider_interactions</c>, e um warning é emitido para investigação manual.
    /// </summary>
    private Dictionary<string, IReadOnlyList<object>> GroupOrdersByStatus(IReadOnlyList<object> orders)
    {
        var byStatus = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var status = ExtractWoStatus(order) ?? "unknown";
            if (!byStatus.TryGetValue(status, out var list))
            {
                list = [];
                byStatus[status] = list;
            }
            list.Add(order);
        }

        var result = new Dictionary<string, IReadOnlyList<object>>();
        foreach (var (key, code, _) in StatusConfigs)
        {
            result[key] = byStatus.Remove(code, out var known) ? known : [];
        }

        if (byStatus.Count > 0)
        {
            var unmappedTotal = byStatus.Values.Sum(list => list.Count);
            logger.LogWarning(
                "[COLETA] {Count} OS com woStatus não mapeado em StatusConfigs ({Statuses}). " +
                "Não entram em statusCounts, mas seguem íntegras no payload bruto de provider_interactions.",
                unmappedTotal, string.Join(", ", byStatus.Keys));
        }

        return result;
    }

    private static string? ExtractWoStatus(object order)
        => order is IDictionary<string, object?> dict && dict.TryGetValue("woStatus", out var status)
            ? status as string
            : null;

    // -------------------------------------------------------------------------
    // Coleta de OS por status (estratégia legada, mantida atrás de
    // CollectorWorkerOptions.UseDateBasedWorkOrderQuery para rollback rápido)
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyList<object>> FetchWorkOrdersForStatusAsync(
        Guid tenantId,
        Guid commandId,
        IPage page,
        RequestTemplate template,
        string statusCode,
        string tabLabel,
        CancellationToken cancellationToken)
    {
        var orders = new List<object>();
        const int maxPages = 50;

        for (var pageNumber = 1; pageNumber <= maxPages; pageNumber++)
        {
            var chunk = await FetchWorkOrdersPageWithInteractionLogAsync(
                tenantId, commandId, page, template, statusCode, pageNumber, cancellationToken);
            orders.AddRange(chunk);
            if (chunk.Count < StatusPageSize) break;
        }

        return orders;
    }

    /// <summary>
    /// Envolve <see cref="FetchWorkOrdersPageAsync"/> registrando exatamente um documento em
    /// <c>provider_interactions</c> por chamada de página (RF-022.1/RN-022.1, critérios de
    /// aceite 1 e 3) — sucesso com o payload bruto em <c>orders</c>, ou falha com
    /// <c>success=false</c> e <c>error_message</c>.
    /// </summary>
    private async Task<IReadOnlyList<object>> FetchWorkOrdersPageWithInteractionLogAsync(
        Guid tenantId,
        Guid commandId,
        IPage page,
        RequestTemplate template,
        string statusCode,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        var request = new Dictionary<string, object?>
        {
            ["url"] = template.Url,
            ["status"] = statusCode,
            ["page"] = pageNumber,
            ["page_size"] = StatusPageSize,
        };

        try
        {
            var orders = await FetchWorkOrdersPageAsync(
                page, template, statusCode, woStatusCond: "eq",
                creationDateFrom: null, creationDateTo: null, pageNumber: pageNumber);
            await LogProviderInteractionSafeAsync(
                tenantId, commandId, "list_query", request, orders, success: true, errorMessage: null, cancellationToken);
            return orders;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await LogProviderInteractionSafeAsync(
                tenantId, commandId, "list_query", request, orders: null, success: false, ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Executa 1 página de busca de OS via <c>fetch</c> no template capturado. Quando
    /// <paramref name="creationDateFrom"/>/<paramref name="creationDateTo"/> são
    /// informados (busca por data, Fase 3), sobrescrevem o intervalo padrão do template
    /// (que reflete apenas os últimos ~30 dias, valor da SPA); quando nulos (estratégia
    /// legada por status), o template mantém seu intervalo original.
    /// </summary>
    private async Task<IReadOnlyList<object>> FetchWorkOrdersPageAsync(
        IPage page,
        RequestTemplate template,
        string statusCode,
        string woStatusCond,
        string? creationDateFrom,
        string? creationDateTo,
        int pageNumber)
    {
        var headersJson = JsonSerializer.Serialize(template.Headers);
        var bodyJson = JsonSerializer.Serialize(template.Body);

        JsonElement result;
        try
        {
            result = await page.EvaluateAsync<JsonElement>(
                @"async ({ templateUrl, headersJson, bodyJson, statusCode, woStatusCond, creationDateFrom, creationDateTo, pageNumber, pageSize }) => {
                    const headers = JSON.parse(headersJson);
                    headers['content-type'] = 'application/json; charset=UTF-8';
                    delete headers['content-length'];
                    delete headers['host'];

                    const body = JSON.parse(bodyJson);
                    body.woStatus = statusCode;
                    body.woStatusCond = woStatusCond;
                    if (creationDateFrom) body.creationDateFrom = creationDateFrom;
                    if (creationDateTo) body.creationDateTo = creationDateTo;
                    body.__page = pageNumber;
                    body.__pagesize = pageSize;

                    const response = await fetch(templateUrl, {
                        method: 'POST',
                        credentials: 'include',
                        headers,
                        body: JSON.stringify(body),
                    });
                    if (response.status === 401) {
                        throw new Error('SESSION_EXPIRED_401');
                    }
                    const payload = await response.json();
                    if (!response.ok || payload.resultCode !== 'ISC-000') {
                        throw new Error('API retornou: ' + (payload.resultCode || response.status) + ' - ' + (payload.resultMsg || ''));
                    }
                    return Array.isArray(payload.data) ? payload.data : [];
                }",
                new
                {
                    templateUrl = template.Url,
                    headersJson,
                    bodyJson,
                    statusCode,
                    woStatusCond,
                    creationDateFrom,
                    creationDateTo,
                    pageNumber,
                    pageSize = StatusPageSize,
                });
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException
            && ex.Message.Contains("SESSION_EXPIRED_401", StringComparison.Ordinal))
        {
            // RF-023.4: sessão rejeitada (401) em pleno voo durante list_query.
            throw new ProviderSessionInvalidException("HTTP 401 em list_query.");
        }

        if (result.ValueKind == JsonValueKind.Array)
        {
            return result.EnumerateArray()
                .Select(item => (object)ConvertJsonElement(item)!)
                .ToList();
        }

        return [];
    }

    /// <summary>
    /// Converte um <see cref="JsonElement"/> em um grafo de objetos .NET nativo
    /// (<see cref="Dictionary{TKey,TValue}"/> para objetos, <see cref="List{T}"/> para
    /// arrays, primitivos para os demais casos). Necessário porque os adapters do
    /// consumer (<see cref="Atua.Collector.Consumer.IProviderInteractionOrderAdapter"/>)
    /// exigem <c>IDictionary&lt;string, object?&gt;</c> para reconhecer e projetar a OS
    /// — <see cref="JsonElement"/> nunca satisfaz esse contrato, o que fazia todas as OS
    /// coletadas serem descartadas silenciosamente.
    /// </summary>
    /// <remarks>
    /// Objetos são montados com um laço manual (sobrescrevendo em vez de usar
    /// <c>.ToDictionary()</c>) porque o payload de detalhe do iService pode conter
    /// chaves duplicadas (ex.: <c>$id</c> aparece 2x) — <c>.ToDictionary()</c> lança
    /// <see cref="ArgumentException"/> nesse caso, descartando o detalhe inteiro da OS.
    /// Ao sobrescrever, mantemos o último valor da chave repetida e preservamos o
    /// restante do objeto.
    /// </remarks>
    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var property in element.EnumerateObject())
                {
                    dict[property.Name] = ConvertJsonElement(property.Value);
                }

                return dict;
            case JsonValueKind.Array:
                return element.EnumerateArray()
                    .Select(ConvertJsonElement)
                    .ToList();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    // -------------------------------------------------------------------------
    // Enriquecimento de OS "assigned" com detalhe
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyList<object>> EnrichAssignedOrdersAsync(
        Guid tenantId,
        Guid commandId,
        IPage page,
        RequestTemplate template,
        IReadOnlyList<object> assignedOrders,
        CancellationToken cancellationToken)
    {
        if (assignedOrders.Count == 0) return assignedOrders;

        var enriched = new List<object>(assignedOrders.Count);
        var headersJson = JsonSerializer.Serialize(template.Headers);

        foreach (var order in assignedOrders)
        {
            var orderJson = JsonSerializer.Serialize(order);
            var workOrderId = TryExtractWorkOrderIdFromOrder(order);
            var request = new Dictionary<string, object?>
            {
                ["url"] = WoDetailUrl,
                ["work_order_id"] = workOrderId,
            };

            try
            {
                var result = await page.EvaluateAsync<JsonElement>(
                    @"async ({ headersJson, orderJson, detailUrl }) => {
                        const order = JSON.parse(orderJson);
                        const workOrderId = order.workOrderId || order.id || null;
                        if (!workOrderId) return order;

                        const headers = JSON.parse(headersJson);
                        headers['content-type'] = 'application/json; charset=UTF-8';
                        delete headers['content-length'];
                        delete headers['host'];

                        const response = await fetch(detailUrl, {
                            method: 'POST',
                            credentials: 'include',
                            headers,
                            body: JSON.stringify({ workOrderId }),
                        });
                        if (response.status === 401) {
                            return { __sessionExpired: true };
                        }
                        const payload = await response.json();
                        if (!response.ok || payload.resultCode !== 'ISC-000') {
                            return order; // falha silenciosa no detalhe, retorna OS sem detalhe
                        }
                        return { ...order, orderDetail: payload.data || null };
                    }",
                    new { headersJson, orderJson, detailUrl = WoDetailUrl });

                var converted = ConvertJsonElement(result)!;

                if (converted is IDictionary<string, object?> convertedDict
                    && convertedDict.TryGetValue("__sessionExpired", out var sessionExpiredFlag)
                    && sessionExpiredFlag is true)
                {
                    // RF-023.4: sessão rejeitada (401) em pleno voo durante detail_query.
                    await LogProviderInteractionSafeAsync(
                        tenantId, commandId, "detail_query", request, orders: null,
                        success: false, "HTTP 401 (sessão expirada)", cancellationToken);
                    throw new ProviderSessionInvalidException("HTTP 401 em detail_query.");
                }

                enriched.Add(converted);
                await LogProviderInteractionSafeAsync(
                    tenantId, commandId, "detail_query", request, [converted],
                    success: true, errorMessage: null, cancellationToken);
            }
            catch (ProviderSessionInvalidException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[DETALHE] Falha ao buscar detalhe de uma OS. Continuando sem detalhe.");
                enriched.Add(order);
                await LogProviderInteractionSafeAsync(
                    tenantId, commandId, "detail_query", request, orders: null,
                    success: false, ex.Message, cancellationToken);
            }
        }

        logger.LogInformation("[DETALHE] {Count} OS designadas com tentativa de enriquecimento.", enriched.Count);
        return enriched;
    }

    /// <summary>
    /// Extrai <c>workOrderId</c>/<c>id</c> de uma OS convertida (<see cref="ConvertJsonElement"/>)
    /// apenas para popular o campo <c>request</c> do documento de auditoria — mesma lógica
    /// de fallback usada no lado JS de <see cref="EnrichAssignedOrdersAsync"/>. Não deve ser
    /// usado para nenhuma decisão de negócio (RF-022.6 delega isso ao consumer).
    /// </summary>
    private static object? TryExtractWorkOrderIdFromOrder(object order)
    {
        if (order is not IDictionary<string, object?> dict) return null;
        if (dict.TryGetValue("workOrderId", out var workOrderId) && workOrderId is not null) return workOrderId;
        return dict.TryGetValue("id", out var id) ? id : null;
    }

    // -------------------------------------------------------------------------
    // Tipos internos
    // -------------------------------------------------------------------------

    private sealed record RequestTemplate(
        string Url,
        IReadOnlyDictionary<string, string> Headers,
        Dictionary<string, object?> Body);
}
