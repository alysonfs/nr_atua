using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Llm;

namespace Atua.Api.Tests;

public class FakeTriagemLlmClientTests
{
    private static TriagemLlmRequest BuildRequest(IReadOnlyList<TriagemLlmChecklistItemState> checklist) => new(
        TenantId: Guid.NewGuid(),
        TriagemId: Guid.NewGuid(),
        TenantNomeComercial: "Posto Teste",
        Produto: "Geladeira",
        Marca: "Consul",
        JaApresentouIdentidade: true,
        ChecklistState: checklist,
        HistoricoConversa: [],
        AnexosRecebidos: []);

    [Fact]
    public async Task RetornaPerguntaDeFallbackDoPrimeiroItemPendente()
    {
        var client = new FakeTriagemLlmClient();
        var checklist = new[]
        {
            new TriagemLlmChecklistItemState(ETriagemChecklistItem.DescricaoDefeito, true),
            new TriagemLlmChecklistItemState(ETriagemChecklistItem.NotaFiscal, false),
        };

        var result = await client.ConduzirAsync(BuildRequest(checklist), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.Success, result.Outcome);
        Assert.Equal(TriagemLlmFallbackQuestions.For(ETriagemChecklistItem.NotaFiscal), result.Payload!.RespostaAoConsumidor);
    }

    [Fact]
    public async Task RetornaMensagemDeConclusaoQuandoChecklistTotalmenteAtendido()
    {
        var client = new FakeTriagemLlmClient();
        var checklist = Enum.GetValues<ETriagemChecklistItem>()
            .Select(item => new TriagemLlmChecklistItemState(item, true))
            .ToArray();

        var result = await client.ConduzirAsync(BuildRequest(checklist), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.Success, result.Outcome);
        Assert.Contains("todas as informações", result.Payload!.RespostaAoConsumidor);
    }

    [Fact]
    public async Task EDeterministico_MesmaEntradaProduzMesmaSaida()
    {
        var checklist = new[] { new TriagemLlmChecklistItemState(ETriagemChecklistItem.Endereco, false) };
        var request = BuildRequest(checklist);

        var client1 = new FakeTriagemLlmClient();
        var client2 = new FakeTriagemLlmClient();
        var result1 = await client1.ConduzirAsync(request, CancellationToken.None);
        var result2 = await client2.ConduzirAsync(request, CancellationToken.None);

        Assert.Equal(result1.Payload!.RespostaAoConsumidor, result2.Payload!.RespostaAoConsumidor);
    }

    [Fact]
    public async Task EnqueueResultSubstituiAHeuristicaPadrao()
    {
        var client = new FakeTriagemLlmClient();
        var programado = TriagemLlmResult.Unavailable("indisponibilidade simulada para teste");
        client.EnqueueResult(programado);

        var result = await client.ConduzirAsync(BuildRequest([]), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.ProviderUnavailable, result.Outcome);
        Assert.Equal("indisponibilidade simulada para teste", result.ErrorReason);
    }
}
