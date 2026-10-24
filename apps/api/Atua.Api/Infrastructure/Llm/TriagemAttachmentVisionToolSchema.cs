using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Define a ferramenta Bedrock Converse (tool use forçado) usada para obter
/// a saída estruturada da classificação de visão computacional de anexos
/// (decisão do PO). Mesmo padrão de <see cref="TriagemLlmToolSchema"/>, mas
/// com esquema próprio, menor e específico.
/// </summary>
internal static class TriagemAttachmentVisionToolSchema
{
    public const string ToolName = TriagemAttachmentVisionPromptBuilder.ToolName;

    public static Tool BuildTool() => new()
    {
        ToolSpec = new ToolSpecification
        {
            Name = ToolName,
            Description = TriagemAttachmentVisionPromptBuilder.BuildToolDescription(),
            InputSchema = new ToolInputSchema { Json = Document.FromObject(BuildJsonSchema()) },
        },
    };

    private static Dictionary<string, object?> BuildJsonSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["tipo"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["enum"] = Enum.GetNames<ETriagemAttachmentVisionTipo>().Cast<object?>().ToList(),
            },
            ["legivel"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["modelo"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Modelo lido na etiqueta — só preenchido quando tipo = Etiqueta e legivel = true.",
            },
            ["numeroSerie"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Número de série lido na etiqueta — só preenchido quando tipo = Etiqueta e legivel = true.",
            },
            ["numeroNotaFiscal"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Número da nota fiscal — só preenchido quando tipo = NotaFiscal e legivel = true.",
            },
            ["serieNotaFiscal"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Série da nota fiscal — só preenchido quando tipo = NotaFiscal e legivel = true.",
            },
            ["dataEmissaoNotaFiscal"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Data de emissão da nota fiscal no formato AAAA-MM-DD — só preenchido quando tipo = NotaFiscal e legivel = true.",
            },
            ["confianca"] = new Dictionary<string, object?>
            {
                ["type"] = "number",
                ["minimum"] = 0,
                ["maximum"] = 1,
            },
        },
        ["required"] = new List<object?> { "tipo", "legivel", "confianca" },
    };
}
