using System.Collections.Concurrent;
using Atua.Collector.Configuration;
using Microsoft.Extensions.Options;

namespace Atua.Collector.Diagnostics;

/// <summary>
/// Implementação de <see cref="IIServiceDebugLogger"/> que escreve, em append, num
/// arquivo de texto plano por ciclo de coleta. Totalmente no-op quando
/// <see cref="CollectorWorkerOptions.EnableLocalDebugLog"/> está desabilitado (padrão
/// em produção/AWS) — nenhuma escrita em disco ocorre nesse caso.
///
/// O caminho do arquivo de cada ciclo é indexado por <c>CommandId</c> num
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> — deliberadamente sem
/// <c>AsyncLocal</c>/estado ambiente implícito. Uma implementação anterior usava
/// <c>AsyncLocal&lt;CycleContext&gt;</c>, setado em <c>BeginCycle</c> e lido por
/// <c>WriteLine</c>; na prática, isso perdia o contexto silenciosamente assim que a
/// escrita partia de um ponto fora da cadeia awaited original (ex.: o handler de evento
/// <c>page.Response</c> do Playwright) — nenhuma linha além de <c>CYCLE_START</c> e o
/// próprio <c>ClaimAsync</c> chegava a ser escrita. Passar o <c>CommandId</c>
/// explicitamente elimina essa classe de bug por completo.
///
/// Falhas de I/O nunca são propagadas — são reportadas via <see cref="ILogger"/> como
/// warning, e a escrita da entrada corrente é simplesmente descartada.
/// </summary>
public sealed class IServiceDebugLogger : IIServiceDebugLogger
{
    private const int MaxEntryLength = 200_000; // ~200KB por entrada de log

    private readonly ConcurrentDictionary<Guid, string> _cyclePaths = new();

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
        _cyclePaths[commandId] = path;

        WriteLine(commandId, "CYCLE_START",
            $"CommandId={commandId} TenantId={tenantId} Username={username}");
    }

    /// <inheritdoc/>
    public void EndCycle(Guid commandId)
    {
        if (!Enabled) return;

        WriteLine(commandId, "CYCLE_END", "Ciclo de coleta encerrado.");
        _cyclePaths.TryRemove(commandId, out _);
    }

    /// <inheritdoc/>
    public void LogApiCall(
        Guid commandId,
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

        WriteLine(commandId, $"API_CALL:{operation}",
            $"{method} {url} StatusCode={ToDisplay(statusCode)}\n" +
            $"  Request: {SensitiveDataRedactor.Truncate(requestJson, MaxEntryLength)}\n" +
            $"  Response: {SensitiveDataRedactor.Truncate(responseJson, MaxEntryLength)}");
    }

    /// <inheritdoc/>
    public void LogLoginStep(Guid commandId, string step, string? url, string result, string? detail = null)
    {
        if (!Enabled) return;

        WriteLine(commandId, $"LOGIN:{step}",
            $"Url={url} Resultado={result}" + (detail is null ? string.Empty : $" Detalhe={detail}"));
    }

    /// <inheritdoc/>
    public void LogTemplateCaptured(Guid commandId, string url, IReadOnlyDictionary<string, string> headers, object? body)
    {
        if (!Enabled) return;

        var redactedHeaders = SensitiveDataRedactor.RedactHeaders(headers);
        var headersJson = SensitiveDataRedactor.RedactAndSerialize(redactedHeaders);
        var bodyJson = SensitiveDataRedactor.RedactAndSerialize(body);

        WriteLine(commandId, "TEMPLATE_CAPTURED",
            $"Url={url}\n" +
            $"  Headers: {headersJson}\n" +
            $"  Body: {bodyJson}");
    }

    /// <inheritdoc/>
    public void LogWorkOrderExchange(Guid commandId, string url, int? statusCode, string? requestBody, string? responseBody)
    {
        if (!Enabled) return;

        var requestJson = SensitiveDataRedactor.RedactJson(requestBody);
        var responseJson = SensitiveDataRedactor.RedactJson(responseBody);

        WriteLine(commandId, "QUERY_WORK_ORDER",
            $"Url={url} StatusCode={ToDisplay(statusCode)}\n" +
            $"  Request: {SensitiveDataRedactor.Truncate(requestJson, MaxEntryLength)}\n" +
            $"  Response: {SensitiveDataRedactor.Truncate(responseJson, MaxEntryLength)}");
    }

    /// <inheritdoc/>
    public void LogEnrichmentExchange(Guid commandId, string url, string? workOrderId, int? statusCode, string? responseBody)
    {
        if (!Enabled) return;

        var responseJson = SensitiveDataRedactor.RedactJson(responseBody);

        WriteLine(commandId, "QUERY_ONE_WORK_ORDER",
            $"Url={url} WorkOrderId={workOrderId} StatusCode={ToDisplay(statusCode)}\n" +
            $"  Response: {SensitiveDataRedactor.Truncate(responseJson, MaxEntryLength)}");
    }

    /// <inheritdoc/>
    public void LogCollectResult(
        Guid commandId,
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

        WriteLine(commandId, "COLLECT_RESULT", detail);
    }

    private static string ToDisplay(int? value) => value?.ToString() ?? "(desconhecido)";

    private void WriteLine(Guid commandId, string eventType, string detail)
    {
        if (!_cyclePaths.TryGetValue(commandId, out var path))
        {
            // Diferente da implementação anterior (baseada em AsyncLocal), a ausência de
            // um ciclo registrado aqui é sempre um sinal de bug real — commandId é
            // sempre conhecido explicitamente por quem chama. Reportamos como warning
            // visível em vez de descartar silenciosamente.
            _logger.LogWarning(
                "[DEBUGLOG] Tentativa de escrever {EventType} para CommandId={CommandId} sem ciclo registrado (BeginCycle não foi chamado ou já encerrou).",
                eventType, commandId);
            return;
        }

        var line = $"[{DateTimeOffset.UtcNow:O}] {eventType} | {detail}{Environment.NewLine}";

        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(path, line);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[DEBUGLOG] Falha ao escrever log de diagnóstico em {Path}.",
                path);
        }
    }
}
