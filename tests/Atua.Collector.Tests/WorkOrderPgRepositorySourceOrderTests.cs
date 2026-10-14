using System.Runtime.CompilerServices;

namespace Atua.Collector.Tests;

/// <summary>
/// Testes estruturais (leitura do código-fonte) que provam que a gravação da
/// intenção de outbox (ADR-041) ocorre, no código, antes da atualização do
/// resume token/commit dentro de <c>ProcessInteractionAsync</c>.
///
/// Limitação reconhecida: não há PostgreSQL real disponível neste projeto de
/// testes, então não é possível provar em runtime a ordem real de execução
/// dentro da mesma transação, nem que uma falha no insert realmente
/// reverte (rollback) os upserts/histórico/resume token já realizados —
/// isso depende inteiramente da transação Postgres (<c>NpgsqlTransaction</c>)
/// já usada por <c>WorkOrderPgRepository.ProcessInteractionAsync</c> (um
/// único <c>try</c>/<c>catch</c> com <c>RollbackAsync</c> no catch, cobrindo
/// qualquer exceção lançada por qualquer um dos comandos da transação,
/// incluindo o novo insert de outbox — não há caminho de código que grave o
/// resume token sem primeiro ter gravado a outbox com sucesso). Mesmo
/// critério de limitação documentado em
/// <c>ProviderInteractionConsumerWorkerTests</c> para a ausência de um
/// PostgreSQL real neste projeto de testes.
/// </summary>
public class WorkOrderPgRepositorySourceOrderTests
{
    [Fact]
    public void InsertTriagemTriggerOutboxOcorreAntesDoUpsertDoResumeToken()
    {
        var source = ReadWorkOrderPgRepositorySource();

        var insertOutboxCallIndex = source.IndexOf("await InsertTriagemTriggerOutboxAsync(", StringComparison.Ordinal);
        var upsertConsumerStateCallIndex =
            source.IndexOf("await UpsertConsumerStateAsync(conn, tx, consumerId, resumeToken",
                StringComparison.Ordinal);

        Assert.True(insertOutboxCallIndex > 0, "Chamada a InsertTriagemTriggerOutboxAsync não encontrada.");
        Assert.True(upsertConsumerStateCallIndex > 0, "Chamada a UpsertConsumerStateAsync não encontrada.");
        Assert.True(insertOutboxCallIndex < upsertConsumerStateCallIndex,
            "A gravação da intenção de outbox deve ocorrer antes da atualização do resume token/commit.");
    }

    [Fact]
    public void InsertTriagemTriggerOutboxEhGatilhadoApenasQuandoStatusElegivel()
    {
        var source = ReadWorkOrderPgRepositorySource();

        Assert.Contains("if (TriagemTriggerEligibility.IsEligible(order.Status))", source);
    }

    [Fact]
    public void ProcessInteractionAsyncTemUmUnicoTryCatchComRollback()
    {
        // Garante que não existe um segundo bloco try/catch "local" ao redor
        // apenas do insert de outbox que pudesse engolir a falha em vez de
        // propagar para o rollback da transação inteira.
        var source = ReadWorkOrderPgRepositorySource();
        var processInteractionBody = ExtractMethodBody(source, "public async Task ProcessInteractionAsync(");

        var tryCount = CountOccurrences(processInteractionBody, "try\n");
        Assert.Equal(1, tryCount);
        Assert.Contains("await tx.RollbackAsync(CancellationToken.None);", processInteractionBody);
    }

    private static string ReadWorkOrderPgRepositorySource([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));
        var repositoryFilePath = Path.Combine(
            repositoryRoot, "apps", "collector", "Atua.Collector", "Consumer", "WorkOrderPgRepository.cs");

        return File.ReadAllText(repositoryFilePath);
    }

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        var startIndex = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(startIndex > 0, $"Assinatura não encontrada: {methodSignature}");

        // Próximo método privado estático após ProcessInteractionAsync delimita o fim do corpo —
        // suficiente para esta assertiva estrutural (não precisa de um parser C# completo).
        var endIndex = source.IndexOf("public async Task AdvanceResumeTokenAsync(", startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, "Fim do método ProcessInteractionAsync não encontrado.");

        return source[startIndex..endIndex];
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
