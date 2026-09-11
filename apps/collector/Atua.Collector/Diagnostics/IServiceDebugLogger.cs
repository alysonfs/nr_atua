using Atua.Collector.Configuration;
using Microsoft.Extensions.Options;

namespace Atua.Collector.Diagnostics;

/// <summary>
/// Implementação de <see cref="IIServiceDebugLogger"/> que escreve, em append, num
/// arquivo de texto plano por ciclo de coleta. Totalmente no-op quando
/// <see cref="CollectorWorkerOptions.EnableLocalDebugLog"/> está desabilitado (padrão
/// em produção/AWS) — nenhuma escrita em disco ocorre nesse caso.
///
/// O contexto do ciclo corrente (caminho do arquivo) é mantido em <see cref="AsyncLocal{T}"/>
/// para que tanto <c>CollectorApiClient</c> (ClaimAsync/CompleteAsync) quanto
/// <c>IServiceCollectorService</c> (login/coleta) escrevam no mesmo arquivo sem precisar
/// repassar o <c>CommandId</c> explicitamente por toda a cadeia de chamadas — ambos são
/// executados na mesma cadeia assíncrona (<c>Worker.RunCycleAsync</c>).
///
/// Falhas de I/O nunca são propagadas — são reportadas via <see cref="ILogger"/> como
/// warning, e a escrita da entrada corrente é simplesmente descartada.
/// </summary>
public sealed class IServiceDebugLogger : IIServiceDebugLogger
{
    private const int MaxEntryLength = 200_000; // ~200KB por entrada de log

    private static readonly AsyncLocal<CycleContext?> CurrentCycle = new();

    private readonly CollectorWorkerOptions _options;
    private readonly ILogger<IServiceDebugLogger> _logger;
    private readonly object _fileLock = new();

    public IServiceDebugLogger(IOptions<CollectorWorkerOptions> options, ILogger<IServiceDebugLogger> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!Enabled) return;

        try
        {
            Directory.CreateDirectory(_options.LocalDebugLogDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[DEBUGLOG] Falha ao criar diretório de log de diagnóstico {Directory}.",
                _options.LocalDebugLogDirectory);
        }
    }

    /// <inheritdoc/>
    public bool Enabled => _options.EnableLocalDebugLog;

    /// <inheritdoc/>
    public void BeginCycle(Guid commandId, Guid? tenantId, string username)
    {
        if (!Enabled) return;

        var fileName = $"iservice-collect-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{commandId}.txt";
        var path = Path.Combine(_options.LocalDebugLogDirectory, fileName);
        CurrentCycle.Value = new CycleContext(path);

        WriteLine("CYCLE_START",
            $"CommandId={commandId} TenantId={tenantId} Username={username}");
    }

    /// <inheritdoc/>
    public void EndCycle()
    {
        if (!Enabled) return;

        WriteLine("CYCLE_END", "Ciclo de coleta encerrado.");
        CurrentCycle.Value = null;
    }

    /// <inheritdoc/>
    public void LogApiCall(
        string operation,
        string method,
        string url,
        int? statusCode,
        object? requestBody,
        object? responseBody)
    {
        if (!Enabled) return;

        var requestJson = SensitiveDataRedactor.RedactAndSerialize(requestBody);
        var responseJson = SensitiveDataRedactor.RedactAndSerialize(responseBody);

        WriteLine($"API_CALL:{operation}",
            $"{method} {url} StatusCode={ToDisplay(statusCode)}\n" +
            $"  Request: {SensitiveDataRedactor.Truncate(requestJson, MaxEntryLength)}\n" +
            $"  Response: {SensitiveDataRedactor.Truncate(responseJson, MaxEntryLength)}");
    }

    /// <inheritdoc/>
    public void LogLoginStep(string step, string? url, string result, string? detail = null)
    {
        if (!Enabled) return;

        WriteLine($"LOGIN:{step}",
            $"Url={url} Resultado={result}" + (detail is null ? string.Empty : $" Detalhe={detail}"));
    }

    /// <inheritdoc/>
    public void LogTemplateCaptured(string url, IReadOnlyDictionary<string, string> headers, object? body)
    {
        if (!Enabled) return;

        var redactedHeaders = SensitiveDataRedactor.RedactHeaders(headers);
        var headersJson = SensitiveDataRedactor.RedactAndSerialize(redactedHeaders);
        var bodyJson = SensitiveDataRedactor.RedactAndSerialize(body);

        WriteLine("TEMPLATE_CAPTURED",
            $"Url={url}\n" +
            $"  Headers: {headersJson}\n" +
            $"  Body: {bodyJson}");
    }

    /// <inheritdoc/>
    public void LogWorkOrderExchange(string url, int? statusCode, string? requestBody, string? responseBody)
    {
        if (!Enabled) return;

        var requestJson = SensitiveDataRedactor.RedactJson(requestBody);
        var responseJson = SensitiveDataRedactor.RedactJson(responseBody);

        WriteLine("QUERY_WORK_ORDER",
            $"Url={url} StatusCode={ToDisplay(statusCode)}\n" +
            $"  Request: {SensitiveDataRedactor.Truncate(requestJson, MaxEntryLength)}\n" +
            $"  Response: {SensitiveDataRedactor.Truncate(responseJson, MaxEntryLength)}");
    }

    /// <inheritdoc/>
    public void LogEnrichmentExchange(string url, string? workOrderId, int? statusCode, string? responseBody)
    {
        if (!Enabled) return;

        var responseJson = SensitiveDataRedactor.RedactJson(responseBody);

        WriteLine("QUERY_ONE_WORK_ORDER",
            $"Url={url} WorkOrderId={workOrderId} StatusCode={ToDisplay(statusCode)}\n" +
            $"  Response: {SensitiveDataRedactor.Truncate(responseJson, MaxEntryLength)}");
    }

    /// <inheritdoc/>
    public void LogCollectResult(
        IReadOnlyDictionary<string, int>? statusCounts,
        bool success,
        string? exceptionType,
        string? exceptionMessage)
    {
        if (!Enabled) return;

        var countsDisplay = statusCounts is null
            ? "(indisponível)"
            : string.Join(", ", statusCounts.Select(kv => $"{kv.Key}={kv.Value}"));

        var detail = success
            ? $"Sucesso. Contagens: {countsDisplay}"
            : $"Falha. ExceptionType={exceptionType} Message={exceptionMessage}";

        WriteLine("COLLECT_RESULT", detail);
    }

    private static string ToDisplay(int? value) => value?.ToString() ?? "(desconhecido)";

    private void WriteLine(string eventType, string detail)
    {
        var cycle = CurrentCycle.Value;
        if (cycle is null)
        {
            // Nenhum ciclo em andamento (ex.: ClaimAsync retornou 204, ou chamada fora
            // de um ciclo de coleta) — não há arquivo para escrever.
            return;
        }

        var line = $"[{DateTimeOffset.UtcNow:O}] {eventType} | {detail}{Environment.NewLine}";

        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(cycle.FilePath, line);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[DEBUGLOG] Falha ao escrever log de diagnóstico em {Path}.",
                cycle.FilePath);
        }
    }

    private sealed record CycleContext(string FilePath);
}
