using System.Text.Json;
using Amazon.Runtime.Documents;

namespace Atua.Api.Tests;

/// <summary>
/// Converte um <see cref="JsonElement"/> (lido do dataset golden em JSON)
/// para <see cref="Document"/> do SDK do Bedrock, simulando o que o
/// <c>ToolUseBlock.Input</c> contém numa resposta real do Converse API. Só
/// usado em testes — não faz parte do caminho de produção.
/// </summary>
internal static class JsonElementDocumentConverter
{
    public static Document ToDocument(JsonElement element) => Document.FromObject(ToObjectGraph(element));

    private static object? ToObjectGraph(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(property => property.Name, property => ToObjectGraph(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(ToObjectGraph).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => throw new NotSupportedException($"Tipo JSON não suportado: {element.ValueKind}"),
    };
}
