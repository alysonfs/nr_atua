using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;
using Atua.Api.Application.Triagens;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Define a ferramenta Bedrock Converse (tool use forçado) usada para obter
/// a saída estruturada da LLM (`triagem-llm-conducao.md` §2). O nome e o
/// esquema JSON devem bater exatamente com o que
/// <see cref="TriagemLlmToolResultParser"/> espera ao desserializar a
/// resposta.
/// </summary>
internal static class TriagemLlmToolSchema
{
    public const string ToolName = TriagemLlmSystemPromptBuilder.ToolName;

    public static Tool BuildTool() => new()
    {
        ToolSpec = new ToolSpecification
        {
            Name = ToolName,
            Description = "Registra a próxima mensagem ao consumidor e os campos estruturados propostos " +
                "para esta triagem. Chamada obrigatória — toda resposta deve usar esta ferramenta.",
            InputSchema = new ToolInputSchema { Json = Document.FromObject(BuildJsonSchema()) },
        },
    };

    private static Dictionary<string, object?> CampoComConfianca() => new()
    {
        ["type"] = "object",
        ["description"] = "Campo estruturado opcional com grau de confiança — omitido quando não identificado.",
        ["properties"] = new Dictionary<string, object?>
        {
            ["valor"] = new Dictionary<string, object?> { ["type"] = "string", ["minLength"] = 1 },
            ["confianca"] = new Dictionary<string, object?>
            {
                ["type"] = "number",
                ["minimum"] = 0,
                ["maximum"] = 1,
            },
        },
        ["required"] = new List<object?> { "valor", "confianca" },
    };

    private static Dictionary<string, object?> BuildJsonSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["respostaAoConsumidor"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["minLength"] = 1,
                ["description"] = "Próxima mensagem a enviar ao consumidor, já no tom definido.",
            },
            ["camposEstruturados"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["tipoDefeito"] = CampoComConfianca(),
                    ["sintoma"] = CampoComConfianca(),
                    ["codigoErro"] = CampoComConfianca(),
                    ["categoria"] = CampoComConfianca(),
                },
            },
            ["itensAtendidos"] = new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["items"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["item"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["enum"] = Enum.GetNames<ETriagemChecklistItem>().Cast<object?>().ToList(),
                        },
                        ["evidencia"] = new Dictionary<string, object?> { ["type"] = "string", ["minLength"] = 1 },
                    },
                    ["required"] = new List<object?> { "item", "evidencia" },
                },
            },
            ["sinalDesvio"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["detectado"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                    ["motivo"] = new Dictionary<string, object?> { ["type"] = "string" },
                },
                ["required"] = new List<object?> { "detectado" },
            },
        },
        ["required"] = new List<object?> { "respostaAoConsumidor", "camposEstruturados", "itensAtendidos", "sinalDesvio" },
    };
}
