using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atua.Collector.Diagnostics;

/// <summary>
/// Utilitário para mascarar dados sensíveis (senha, cookies, tokens de autorização)
/// antes de escrevê-los no log de diagnóstico local (<see cref="IIServiceDebugLogger"/>).
/// Nunca deve deixar vazar valores sensíveis, mesmo em payloads arbitrários vindos do
/// iService ou da API Atua.
/// </summary>
public static class SensitiveDataRedactor
{
    private const string RedactedValue = "[REDACTED]";
    private static readonly string[] SensitiveKeyFragments = ["password", "senha"];
    private static readonly string[] SensitiveHeaderFragments = ["cookie", "authorization"];

    /// <summary>
    /// Retorna uma cópia do dicionário de headers com os valores de headers sensíveis
    /// (cookie/authorization) substituídos por "[REDACTED]", preservando o nome do header.
    /// </summary>
    public static IReadOnlyDictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null) return new Dictionary<string, string>();

        return headers.ToDictionary(
            kv => kv.Key,
            kv => IsSensitiveHeaderName(kv.Key) ? RedactedValue : kv.Value);
    }

    private static bool IsSensitiveHeaderName(string name) =>
        SensitiveHeaderFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Recebe um objeto JSON serializável (ex.: <c>Dictionary&lt;string, object?&gt;</c>) e
    /// retorna a representação JSON com campos sensíveis (password/senha) mascarados.
    /// </summary>
    public static string RedactAndSerialize(object? value)
    {
        if (value is null) return "null";
        try
        {
            var json = JsonSerializer.Serialize(value);
            return RedactJson(json);
        }
        catch (Exception ex)
        {
            return $"[FALHA_AO_SERIALIZAR: {ex.GetType().Name}: {ex.Message}]";
        }
    }

    /// <summary>
    /// Recebe uma string JSON bruta (ex.: corpo de resposta HTTP) e retorna a mesma
    /// estrutura com campos sensíveis (password/senha) mascarados. Se o conteúdo não for
    /// um JSON válido, retorna o texto original inalterado (não há chave a mascarar).
    /// </summary>
    public static string RedactJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return rawJson ?? string.Empty;

        try
        {
            var node = JsonNode.Parse(rawJson);
            RedactNode(node);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? rawJson;
        }
        catch (JsonException)
        {
            // Não é JSON — devolve como texto puro (ex.: corpo vazio, HTML de erro, etc.)
            return rawJson;
        }
    }

    private static void RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (IsSensitiveKeyName(key))
                    {
                        obj[key] = RedactedValue;
                    }
                    else
                    {
                        RedactNode(obj[key]);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    RedactNode(item);
                }
                break;
        }
    }

    private static bool IsSensitiveKeyName(string name) =>
        SensitiveKeyFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Trunca conteúdo textual além de <paramref name="maxLength"/> caracteres, anexando
    /// um aviso explícito de truncamento — usado para evitar arquivos de log gigantes
    /// (ex.: payloads muito grandes de queryWorkOrder).
    /// </summary>
    public static string Truncate(string? content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength) return content ?? string.Empty;

        return content[..maxLength] +
               $"...[TRUNCADO - {content.Length - maxLength} caracteres omitidos, limite de {maxLength} atingido]";
    }
}
