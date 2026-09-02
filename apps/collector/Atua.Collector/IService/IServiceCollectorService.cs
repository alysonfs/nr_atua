using System.Text.Json;
using Atua.Collector.Configuration;
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
/// para que <c>Persistence.WorkOrderRepository</c> consiga reconhecê-las como dicionário.
/// </summary>
public sealed class IServiceCollectorService(
    IOptions<CollectorWorkerOptions> options,
    ILogger<IServiceCollectorService> logger)
    : IIServiceCollector
{
    private const string LoginUrl = "https://signin.midea.com/login?service=https://ics-amer.midea.com/";
    private const string IServiceHost = "ics-amer.midea.com";
    private const string SigninHost = "signin.midea.com";
    private const string WoListUrl = "https://ics-amer.midea.com/web/iservice-wom/workOrder/queryWorkOrder";
    private const string WoDetailUrl = "https://ics-amer.midea.com/web/iservice-wom/workOrder/queryOneWorkOrder";
    private const int StatusPageSize = 200;

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

        var page = await browser.NewPageAsync(new BrowserNewPageOptions
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
        });

        // Guard somente-leitura: bloqueia POSTs de escrita no iService
        await SetupReadOnlyGuardAsync(page);

        try
        {
            await DoLoginAsync(page, username, password, cancellationToken);

            logger.LogInformation("[COLETOR] Abrindo visão por status de OS...");
            await page.GotoAsync($"https://{IServiceHost}/",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = PageTimeout });

            cancellationToken.ThrowIfCancellationRequested();

            var requestTemplate = await OpenWorkOrderStatusViewAsync(page);

            logger.LogInformation("[COLETOR] Template da API capturado. Iniciando coleta por status...");
            cancellationToken.ThrowIfCancellationRequested();

            var ordersByStatus = new Dictionary<string, IReadOnlyList<object>>();
            var statusCounts = new Dictionary<string, int>();

            foreach (var (key, code, tabLabel) in StatusConfigs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var orders = await FetchWorkOrdersForStatusAsync(page, requestTemplate, code, tabLabel);
                ordersByStatus[key] = orders;
                statusCounts[key] = orders.Count;
                logger.LogInformation("[COLETA] {Count} OS '{TabLabel}' coletadas.", orders.Count, tabLabel);
            }

            // Enriquecimento: apenas OS "assigned" recebem detalhe
            var assignedOrders = await EnrichAssignedOrdersAsync(page, requestTemplate, ordersByStatus["assigned"]);
            ordersByStatus["assigned"] = assignedOrders;

            logger.LogInformation(
                "[RESULTADO] Designado={Assigned} | Em Processamento={Accepted} | " +
                "Pendente={Pending} | Concluído={Closed} | Cancelado={Cancelled}",
                statusCounts["assigned"],
                statusCounts["accepted"],
                statusCounts["pending"],
                statusCounts["closed"],
                statusCounts["cancelled"]);

            return new CollectionResult(
                DateTimeOffset.UtcNow,
                statusCounts,
                ordersByStatus);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

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

    private static bool IsWriteRequestOnIService(string method, string url)
    {
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Host != IServiceHost) return false;
        if (!uri.AbsolutePath.StartsWith("/web/iservice-wom/workOrder/", StringComparison.OrdinalIgnoreCase)) return false;

        var endpoint = uri.AbsolutePath.Split('/').LastOrDefault() ?? string.Empty;
        return !endpoint.StartsWith("query", StringComparison.OrdinalIgnoreCase)
            && !endpoint.StartsWith("get", StringComparison.OrdinalIgnoreCase)
            && !endpoint.StartsWith("select", StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Login CAS
    // -------------------------------------------------------------------------

    private async Task DoLoginAsync(IPage page, string username, string password, CancellationToken cancellationToken)
    {
        // NUNCA logar password
        logger.LogInformation("[LOGIN] Iniciando fluxo de login CAS para usuário={Username}.", username);

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
            await CaptureDebugArtifactsAsync(page, "login-goto-timeout");
            throw new IServiceUnavailableEx("Timeout ao carregar página de login CAS.", ex);
        }

        cancellationToken.ThrowIfCancellationRequested();

        const string usernameSelector = "input[placeholder='Conta']";
        const string passwordSelector = "input[placeholder='Senha']";

        try
        {
            await page.WaitForSelectorAsync(usernameSelector, new PageWaitForSelectorOptions { Timeout = PageTimeout });
        }
        catch (TimeoutException ex)
        {
            await CaptureDebugArtifactsAsync(page, "login-username-timeout");
            throw new IServiceUnavailableEx("Timeout aguardando campo de usuário na página de login CAS.", ex);
        }

        await page.ClickAsync(usernameSelector);
        await page.FillAsync(usernameSelector, username);

        await page.ClickAsync(passwordSelector);
        await page.FillAsync(passwordSelector, password);

        logger.LogInformation("[LOGIN] Submetendo formulário...");
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
                throw new CredentialRejectedEx($"Login CAS rejeitado para o usuário '{username}'.");
            }

            throw new IServiceUnavailableEx("Timeout aguardando redirecionamento pós-login para o iService.");
        }

        logger.LogInformation("[LOGIN] Autenticado com sucesso no iService. URL={Url}", page.Url);
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
    // Coleta de OS por status
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyList<object>> FetchWorkOrdersForStatusAsync(
        IPage page,
        RequestTemplate template,
        string statusCode,
        string tabLabel)
    {
        var orders = new List<object>();
        const int maxPages = 50;

        for (var pageNumber = 1; pageNumber <= maxPages; pageNumber++)
        {
            var chunk = await FetchWorkOrdersPageAsync(page, template, statusCode, pageNumber);
            orders.AddRange(chunk);
            if (chunk.Count < StatusPageSize) break;
        }

        return orders;
    }

    private async Task<IReadOnlyList<object>> FetchWorkOrdersPageAsync(
        IPage page,
        RequestTemplate template,
        string statusCode,
        int pageNumber)
    {
        var headersJson = JsonSerializer.Serialize(template.Headers);
        var bodyJson = JsonSerializer.Serialize(template.Body);

        var result = await page.EvaluateAsync<JsonElement>(
            @"async ({ templateUrl, headersJson, bodyJson, statusCode, pageNumber, pageSize }) => {
                const headers = JSON.parse(headersJson);
                headers['content-type'] = 'application/json; charset=UTF-8';
                delete headers['content-length'];
                delete headers['host'];

                const body = JSON.parse(bodyJson);
                body.woStatus = statusCode;
                body.__page = pageNumber;
                body.__pagesize = pageSize;

                const response = await fetch(templateUrl, {
                    method: 'POST',
                    credentials: 'include',
                    headers,
                    body: JSON.stringify(body),
                });
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
                pageNumber,
                pageSize = StatusPageSize,
            });

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
    /// arrays, primitivos para os demais casos). Necessário porque
    /// <c>Persistence.WorkOrderRepository</c> exige <c>IDictionary&lt;string, object?&gt;</c>
    /// para reconhecer e persistir a OS — <see cref="JsonElement"/> nunca satisfaz esse
    /// contrato, o que fazia todas as OS coletadas serem descartadas silenciosamente.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
        JsonValueKind.Array => element.EnumerateArray()
            .Select(ConvertJsonElement)
            .ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    // -------------------------------------------------------------------------
    // Enriquecimento de OS "assigned" com detalhe
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyList<object>> EnrichAssignedOrdersAsync(
        IPage page,
        RequestTemplate template,
        IReadOnlyList<object> assignedOrders)
    {
        if (assignedOrders.Count == 0) return assignedOrders;

        var enriched = new List<object>(assignedOrders.Count);
        var headersJson = JsonSerializer.Serialize(template.Headers);

        foreach (var order in assignedOrders)
        {
            var orderJson = JsonSerializer.Serialize(order);

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
                        const payload = await response.json();
                        if (!response.ok || payload.resultCode !== 'ISC-000') {
                            return order; // falha silenciosa no detalhe, retorna OS sem detalhe
                        }
                        return { ...order, orderDetail: payload.data || null };
                    }",
                    new { headersJson, orderJson, detailUrl = WoDetailUrl });

                enriched.Add(ConvertJsonElement(result)!);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[DETALHE] Falha ao buscar detalhe de uma OS. Continuando sem detalhe.");
                enriched.Add(order);
            }
        }

        logger.LogInformation("[DETALHE] {Count} OS designadas com tentativa de enriquecimento.", enriched.Count);
        return enriched;
    }

    // -------------------------------------------------------------------------
    // Tipos internos
    // -------------------------------------------------------------------------

    private sealed record RequestTemplate(
        string Url,
        IReadOnlyDictionary<string, string> Headers,
        Dictionary<string, object?> Body);
}
