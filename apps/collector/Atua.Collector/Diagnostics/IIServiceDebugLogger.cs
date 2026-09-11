namespace Atua.Collector.Diagnostics;

/// <summary>
/// Logger de diagnóstico local do ciclo de coleta iService — escreve, em arquivo de
/// texto plano, o máximo de detalhe possível de cada requisição/resposta do ciclo
/// (login CAS, queryWorkOrder, queryOneWorkOrder, ClaimAsync/CompleteAsync).
///
/// Habilitado exclusivamente via <c>CollectorWorkerOptions.EnableLocalDebugLog</c> —
/// NUNCA deve estar ativo em produção/AWS. Quando desabilitado, todas as implementações
/// devem ser no-op (nenhuma escrita em disco, nenhum overhead de serialização).
///
/// Nenhum método desta interface deve lançar exceção que interrompa o fluxo real de
/// coleta — falhas de I/O devem ser tratadas internamente e reportadas via
/// <c>ILogger</c> como warning.
/// </summary>
public interface IIServiceDebugLogger
{
    /// <summary>Indica se o log de diagnóstico está habilitado (via configuração).</summary>
    bool Enabled { get; }

    /// <summary>
    /// Inicia um novo ciclo de coleta, abrindo (logicamente) o arquivo de log
    /// correspondente. Deve ser chamado assim que o <c>CommandId</c> for conhecido
    /// (logo após um <c>ClaimAsync</c> bem-sucedido).
    /// </summary>
    void BeginCycle(Guid commandId, Guid? tenantId, string username);

    /// <summary>Encerra o ciclo de coleta corrente, registrando o encerramento no log.</summary>
    void EndCycle();

    /// <summary>Loga uma chamada feita à nossa própria API Atua (ClaimAsync/CompleteAsync).</summary>
    void LogApiCall(
        string operation,
        string method,
        string url,
        int? statusCode,
        object? requestBody,
        object? responseBody);

    /// <summary>Loga uma etapa do fluxo de login CAS.</summary>
    void LogLoginStep(string step, string? url, string result, string? detail = null);

    /// <summary>Loga o template de request (queryWorkOrder) capturado durante a navegação.</summary>
    void LogTemplateCaptured(string url, IReadOnlyDictionary<string, string> headers, object? body);

    /// <summary>Loga uma troca (request/response) de queryWorkOrder por status/página.</summary>
    void LogWorkOrderExchange(string url, int? statusCode, string? requestBody, string? responseBody);

    /// <summary>Loga uma troca (request/response) de enriquecimento (queryOneWorkOrder) por OS.</summary>
    void LogEnrichmentExchange(string url, string? workOrderId, int? statusCode, string? responseBody);

    /// <summary>Loga o resultado final do ciclo de coleta (contagens e sucesso/falha).</summary>
    void LogCollectResult(
        IReadOnlyDictionary<string, int>? statusCounts,
        bool success,
        string? exceptionType,
        string? exceptionMessage);
}
