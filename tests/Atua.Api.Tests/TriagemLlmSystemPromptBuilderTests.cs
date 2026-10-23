using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

public class TriagemLlmSystemPromptBuilderTests
{
    private static TriagemLlmRequest BuildRequest(bool jaApresentouIdentidade = false) => new(
        TenantId: Guid.NewGuid(),
        TriagemId: Guid.NewGuid(),
        TenantNomeComercial: "Posto Natal Refrigeração",
        Produto: "Ar-condicionado Split Inverter",
        Marca: "Midea",
        JaApresentouIdentidade: jaApresentouIdentidade,
        ChecklistState:
        [
            new TriagemLlmChecklistItemState(ETriagemChecklistItem.DescricaoDefeito, true),
            new TriagemLlmChecklistItemState(ETriagemChecklistItem.NotaFiscal, false),
        ],
        HistoricoConversa: [],
        AnexosRecebidos: []);

    [Fact]
    public void ContemAsRegrasProibitivasObrigatorias()
    {
        var prompt = TriagemLlmSystemPromptBuilder.Build(BuildRequest());

        Assert.Contains("NUNCA agenda, cancela ou conclui", prompt);
        Assert.Contains("NUNCA classifica ou menciona o SubStatus", prompt);
        Assert.Contains("NUNCA promete prazo, preço, orçamento ou garantia", prompt);
        Assert.Contains("NUNCA faz diagnóstico técnico", prompt);
        Assert.Contains("NUNCA envia links, nem pede CPF, senha ou dados de cartão", prompt);
    }

    [Fact]
    public void IncluiApresentacaoApenasQuandoAindaNaoApresentada()
    {
        var promptSemApresentacao = TriagemLlmSystemPromptBuilder.Build(BuildRequest(jaApresentouIdentidade: false));
        var promptComApresentacao = TriagemLlmSystemPromptBuilder.Build(BuildRequest(jaApresentouIdentidade: true));

        Assert.Contains("Sou a assistente virtual da Posto Natal Refrigeração", promptSemApresentacao);
        Assert.Contains("Você já se apresentou nesta triagem. Não se apresente novamente.", promptComApresentacao);
        Assert.DoesNotContain("Sou a assistente virtual da Posto Natal Refrigeração", promptComApresentacao);
    }

    [Fact]
    public void ListaItensDoChecklistComSeuEstado()
    {
        var prompt = TriagemLlmSystemPromptBuilder.Build(BuildRequest());

        Assert.Contains("DescricaoDefeito: já atendido", prompt);
        Assert.Contains("NotaFiscal: pendente", prompt);
    }

    [Fact]
    public void IncluiProdutoEMarcaMasNuncaDadosPessoais()
    {
        var prompt = TriagemLlmSystemPromptBuilder.Build(BuildRequest());

        Assert.Contains("Produto: Ar-condicionado Split Inverter", prompt);
        Assert.Contains("Marca: Midea", prompt);
        Assert.DoesNotContain("CPF:", prompt);
        Assert.DoesNotContain("Telefone:", prompt);
    }

    [Fact]
    public void ExigeChamadaDaFerramentaObrigatoria()
    {
        var prompt = TriagemLlmSystemPromptBuilder.Build(BuildRequest());

        Assert.Contains($"`{TriagemLlmSystemPromptBuilder.ToolName}`", prompt);
    }

    [Fact]
    public void ProibeAfirmarQueViuAnexoERepetirPerguntas()
    {
        var prompt = TriagemLlmSystemPromptBuilder.Build(BuildRequest());

        Assert.Contains("você NÃO vê o conteúdo dos anexos", prompt);
        Assert.Contains("Nunca repita uma pergunta que o cliente já respondeu", prompt);
        Assert.Contains("Não investigue o defeito", prompt);
    }
}
