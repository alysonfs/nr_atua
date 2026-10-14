using Atua.Collector.Consumer;

namespace Atua.Collector.Tests;

/// <summary>
/// Testes estruturais (assertivas sobre o texto SQL) de
/// <see cref="TriagemTriggerOutboxSql.InsertPendingCommandText"/> (ADR-041).
///
/// Limitação reconhecida: não há PostgreSQL real neste teste — por isso ele
/// não prova o comportamento real de <c>ON CONFLICT DO NOTHING</c> (ex.: que
/// duas tentativas concorrentes de insert para a mesma OS realmente
/// deduplicam no nível do banco) nem a ordem real de commit da transação.
/// Esses dois pontos (schema compartilhado com a Master API + ordenação
/// antes do resume token) são cobertos, respectivamente, por este teste
/// estrutural e por <see cref="WorkOrderPgRepositorySourceOrderTests"/>
/// (ordem de chamadas via fake de <c>IWorkOrderPgRepository</c> não se
/// aplica aqui, pois a gravação é SQL direto dentro do próprio repositório —
/// ver limitação documentada também em
/// <c>ProviderInteractionConsumerWorkerTests</c>, que testa apenas a
/// interface, não o SQL interno do repositório Postgres).
/// </summary>
public class TriagemTriggerOutboxSqlTests
{
    [Fact]
    public void UsaOnConflictDoNothingNaChaveDeIdempotencia()
    {
        var sql = TriagemTriggerOutboxSql.InsertPendingCommandText;

        Assert.Contains(
            "ON CONFLICT (\"TenantId\", \"IntegrationId\", \"WorkOrderProviderId\") DO NOTHING", sql);
    }

    [Fact]
    public void InsereNaTabelaEColunasEsperadasPelaMasterApi()
    {
        var sql = TriagemTriggerOutboxSql.InsertPendingCommandText;

        Assert.Contains("INSERT INTO triagem_trigger_outbox", sql);
        foreach (var column in new[]
                 {
                     "\"Id\"", "\"TenantId\"", "\"IntegrationId\"", "\"WorkOrderId\"", "\"WorkOrderProviderId\"",
                     "\"OccurredAtUtc\"", "\"Status\"", "\"AttemptCount\"", "\"NextAttemptAtUtc\"", "\"CreatedAtUtc\"",
                 })
        {
            Assert.Contains(column, sql);
        }
    }

    [Fact]
    public void GravaComoPendingEZeroTentativas()
    {
        var sql = TriagemTriggerOutboxSql.InsertPendingCommandText;

        Assert.Contains("'Pending'", sql);
        Assert.Contains(", 0,", sql);
    }
}
