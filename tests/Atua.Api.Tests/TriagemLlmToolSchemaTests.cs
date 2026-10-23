using Atua.Api.Infrastructure.Llm;

namespace Atua.Api.Tests;

public class TriagemLlmToolSchemaTests
{
    [Fact]
    public void BuildToolUsaONomeEsperadoPeloVerificadorDeToolUse()
    {
        var tool = TriagemLlmToolSchema.BuildTool();

        Assert.Equal(TriagemLlmToolSchema.ToolName, tool.ToolSpec.Name);
        Assert.Equal("registrar_triagem", tool.ToolSpec.Name);
    }

    [Fact]
    public void InputSchemaDeclaraTodosOsCamposObrigatoriosDoContrato()
    {
        var tool = TriagemLlmToolSchema.BuildTool();
        var schema = tool.ToolSpec.InputSchema.Json;

        Assert.True(schema.IsDictionary());
        var root = schema.AsDictionary();
        Assert.True(root.TryGetValue("properties", out var properties));
        Assert.True(properties.IsDictionary());

        var propertyNames = properties.AsDictionary().Keys;
        Assert.Contains("respostaAoConsumidor", propertyNames);
        Assert.Contains("camposEstruturados", propertyNames);
        Assert.Contains("itensAtendidos", propertyNames);
        Assert.Contains("sinalDesvio", propertyNames);

        Assert.True(root.TryGetValue("required", out var required));
        var requiredNames = required.AsList().Select(item => item.AsString()).ToList();
        Assert.Equal(
            new[] { "respostaAoConsumidor", "camposEstruturados", "itensAtendidos", "sinalDesvio" },
            requiredNames);
    }
}
