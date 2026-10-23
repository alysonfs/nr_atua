using Atua.Api.Infrastructure.Llm;

namespace Atua.Api.Tests;

/// <summary>
/// Testes do parser do esquema da ferramenta Bedrock
/// (<see cref="TriagemLlmToolResultParser"/>) sobre o dataset golden
/// sintético — cobre casos válidos, com campos ausentes e com valores fora
/// do intervalo permitido.
/// </summary>
public class TriagemLlmToolResultParserGoldenTests
{
    public static IEnumerable<object[]> Cases() =>
        TriagemLlmGoldenDataset.LoadCases().Select(golden => new object[] { golden });

    [Theory]
    [MemberData(nameof(Cases))]
    public void ParseRespeitaOResultadoEsperadoDoDataset(TriagemLlmGoldenCase golden)
    {
        var document = JsonElementDocumentConverter.ToDocument(golden.ToolInputJson);

        var parsed = TriagemLlmToolResultParser.TryParse(document, out var payload, out var error);

        Assert.Equal(golden.SchemaValido, parsed);

        if (golden.SchemaValido)
        {
            Assert.NotNull(payload);
            Assert.Null(error);
            Assert.False(string.IsNullOrWhiteSpace(payload!.RespostaAoConsumidor));
        }
        else
        {
            Assert.Null(payload);
            Assert.NotNull(error);
            if (golden.MotivoInvalidoEsperado is not null)
            {
                Assert.Equal(golden.MotivoInvalidoEsperado, error);
            }
        }
    }

    [Fact]
    public void DatasetContemPeloMenosDezCasos()
    {
        var cases = TriagemLlmGoldenDataset.LoadCases();
        Assert.True(cases.Count >= 10, $"Esperado ao menos 10 casos no dataset golden, encontrados {cases.Count}.");
    }

    [Fact]
    public void DatasetContemCasosValidosEInvalidos()
    {
        var cases = TriagemLlmGoldenDataset.LoadCases();
        Assert.Contains(cases, c => c.SchemaValido);
        Assert.Contains(cases, c => !c.SchemaValido);
    }
}
