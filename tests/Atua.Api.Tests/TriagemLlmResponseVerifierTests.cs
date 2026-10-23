using Atua.Api.Application.Triagens;

namespace Atua.Api.Tests;

public class TriagemLlmResponseVerifierTests
{
    private readonly TriagemLlmResponseVerifier verifier = new();

    [Theory]
    [InlineData("Obrigado! Pode me enviar uma foto da nota fiscal?")]
    [InlineData("Entendi, pode me contar mais sobre o problema?")]
    [InlineData("Perfeito, consegui ler a etiqueta! Agora falta a localização.")]
    public void PermiteRespostasDentroDoEscopoDaTriagem(string resposta)
    {
        var result = verifier.Verify(resposta);

        Assert.True(result.Permitido);
        Assert.Null(result.Motivo);
    }

    [Theory]
    [InlineData("Vou agendar a visita para amanhã às 10h.", "promessa de agendamento")]
    [InlineData("Posso cancelar o atendimento se preferir.", "promessa de cancelamento")]
    [InlineData("Já vamos concluir a OS agora mesmo.", "promessa de conclusão da OS")]
    [InlineData("O prazo para isso é de 48 horas.", "promessa de prazo")]
    [InlineData("O orçamento do serviço é R$150.", "promessa de preço/orçamento")]
    [InlineData("O produto ainda está na garantia.", "promessa de garantia")]
    [InlineData("Veja mais em https://exemplo.com/produto", "link")]
    [InlineData("Pode me informar seu CPF para continuar?", "pedido de CPF")]
    [InlineData("Qual é a sua senha de acesso?", "pedido de senha")]
    [InlineData("Pode me passar os dados do cartão?", "pedido de dados de cartão")]
    public void BloqueiaRespostasForaDoEscopoOuComPromessaIndevida(string resposta, string motivoEsperado)
    {
        var result = verifier.Verify(resposta);

        Assert.False(result.Permitido);
        Assert.Equal(motivoEsperado, result.Motivo);
    }

    [Fact]
    public void BloqueiaRespostaVazia()
    {
        var result = verifier.Verify("   ");

        Assert.False(result.Permitido);
        Assert.Equal("resposta vazia", result.Motivo);
    }

    [Fact]
    public void BloqueiaRespostaAcimaDoLimiteDeTamanho()
    {
        var resposta = new string('a', TriagemLlmResponseVerifier.MaxLength + 1);

        var result = verifier.Verify(resposta);

        Assert.False(result.Permitido);
        Assert.Contains("excede o limite", result.Motivo);
    }

    [Fact]
    public void TodasAsPerguntasDeFallbackSaoPermitidasPeloVerificador()
    {
        foreach (var item in Enum.GetValues<Domain.Triagens.ETriagemChecklistItem>())
        {
            var pergunta = TriagemLlmFallbackQuestions.For(item);

            var result = verifier.Verify(pergunta);

            Assert.True(result.Permitido, $"Pergunta de fallback para '{item}' deveria ser permitida: {result.Motivo}");
        }
    }

    [Fact]
    public void DatasetGolden_RespostaComPromessaProibidaEBloqueada()
    {
        var golden = TriagemLlmGoldenDataset.LoadCases()
            .Single(c => c.CaseId == "golden-10-resposta-com-promessa-proibida");
        var resposta = golden.ToolInputJson.GetProperty("respostaAoConsumidor").GetString()!;

        var result = verifier.Verify(resposta);

        Assert.Equal(golden.RespostaDeveSerBloqueada, !result.Permitido);
        Assert.Equal(golden.MotivoBloqueioEsperado, result.Motivo);
    }
}
