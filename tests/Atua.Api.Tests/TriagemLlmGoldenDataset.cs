using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atua.Api.Tests;

/// <summary>
/// Carrega o dataset golden sintético de conversas de triagem (sem dados
/// reais) usado para testar o parser do esquema da ferramenta Bedrock e o
/// verificador determinístico da resposta ao consumidor — ver
/// <c>TestData/triagem-llm-golden-conversations.json</c>.
/// </summary>
public static class TriagemLlmGoldenDataset
{
    public static IReadOnlyList<TriagemLlmGoldenCase> LoadCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "triagem-llm-golden-conversations.json");
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<TriagemLlmGoldenDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Dataset golden vazio ou malformado.");
        return document.Casos;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class TriagemLlmGoldenDocument
    {
        public List<TriagemLlmGoldenCase> Casos { get; set; } = [];
    }
}

public sealed class TriagemLlmGoldenCase
{
    public string CaseId { get; set; } = "";
    public string Cenario { get; set; } = "";
    public string Produto { get; set; } = "";
    public string? Marca { get; set; }
    public List<TriagemLlmGoldenChecklistItem> Checklist { get; set; } = [];
    public List<TriagemLlmGoldenTurn> Historico { get; set; } = [];
    public List<TriagemLlmGoldenAnexo> Anexos { get; set; } = [];
    public bool SchemaValido { get; set; }
    public string? MotivoInvalidoEsperado { get; set; }
    public bool RespostaDeveSerBloqueada { get; set; }
    public string? MotivoBloqueioEsperado { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    // Nome deliberadamente diferente de "ferramentaInput" (a chave JSON):
    // uma propriedade computada cujo nome colida, mesmo sem distinção de
    // maiúsculas/minúsculas, com uma chave do JSON impede que o
    // System.Text.Json capture essa chave em `ExtensionData` — mesmo com
    // `[JsonIgnore]` na propriedade computada.
    public JsonElement ToolInputJson => ExtensionData is not null && ExtensionData.TryGetValue("ferramentaInput", out var value)
        ? value
        : throw new InvalidOperationException($"Caso '{CaseId}' não possui 'ferramentaInput'.");
}

public sealed class TriagemLlmGoldenChecklistItem
{
    public string Item { get; set; } = "";
    public bool Atendido { get; set; }
}

public sealed class TriagemLlmGoldenTurn
{
    public string Role { get; set; } = "";
    public string Texto { get; set; } = "";
}

public sealed class TriagemLlmGoldenAnexo
{
    public string MediaId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? ChecklistItemCandidato { get; set; }
}
