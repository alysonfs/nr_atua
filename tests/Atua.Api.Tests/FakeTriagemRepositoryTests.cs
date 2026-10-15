using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Testes dedicados à semântica de concorrência otimista de
/// <see cref="FakeTriagemRepository"/> — provam, de forma determinística e
/// sem depender de orquestrar threads reais, que duas leituras
/// independentes (clones) da mesma triagem arbitram corretamente qual
/// transição vence e que o perdedor nunca sobrescreve o vencedor
/// silenciosamente (mesma semântica de <c>IsConcurrencyToken()</c> usada por
/// <c>TriagemRepository</c> real contra PostgreSQL).
/// </summary>
public class FakeTriagemRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetOrAddAsync_DevolveClonesIndependentes_MutarUmNaoAfetaOutro()
    {
        var repository = new FakeTriagemRepository();
        var key = new TriagemIdempotencyKey(Guid.NewGuid(), Guid.NewGuid(), "OS-1");
        var created = await repository.GetOrAddAsync(key,
            new Triagem(Guid.NewGuid(), key.TenantId, key.IntegrationId, Guid.NewGuid(), Now), CancellationToken.None);

        var readerA = await repository.FindByKeyAsync(key, CancellationToken.None);
        var readerB = await repository.FindByKeyAsync(key, CancellationToken.None);

        Assert.NotNull(readerA);
        Assert.NotNull(readerB);
        Assert.NotSame(readerA, readerB);
        Assert.NotSame(created.Triagem, readerA);

        readerA!.MarcarContatoIniciado(Now);

        // A mutação em readerA não deve ser visível em readerB nem no
        // registro ainda "committed" — cada leitura é uma materialização
        // independente, como uma consulta real ao banco.
        Assert.Equal(ETriagemStatus.AguardandoEnvio, readerB!.Status);
        var stillCommitted = await repository.FindByKeyAsync(key, CancellationToken.None);
        Assert.Equal(ETriagemStatus.AguardandoEnvio, stillCommitted!.Status);
    }

    [Fact]
    public async Task SaveTransitionAsync_DuasLeiturasConcorrentes_SegundaFalhaEReloadRetornaVencedor()
    {
        var repository = new FakeTriagemRepository();
        var key = new TriagemIdempotencyKey(Guid.NewGuid(), Guid.NewGuid(), "OS-1");
        await repository.GetOrAddAsync(key,
            new Triagem(Guid.NewGuid(), key.TenantId, key.IntegrationId, Guid.NewGuid(), Now), CancellationToken.None);

        // Duas execuções concorrentes "leem" (clonam) a mesma triagem antes
        // de qualquer uma delas persistir — ambas capturam o mesmo token de
        // concorrência original.
        var winnerRead = await repository.FindByKeyAsync(key, CancellationToken.None);
        var loserRead = await repository.FindByKeyAsync(key, CancellationToken.None);
        Assert.NotNull(winnerRead);
        Assert.NotNull(loserRead);

        winnerRead!.MarcarContatoIniciado(Now);
        loserRead!.MarcarContatoIniciado(Now);

        // O vencedor persiste primeiro — sucesso.
        await repository.SaveTransitionAsync(winnerRead, CancellationToken.None);

        // O perdedor tenta persistir a mesma transição em cima do estado
        // ORIGINAL (já superado) — deve falhar com a exceção tipada, nunca
        // sobrescrever silenciosamente o vencedor.
        var exception = await Assert.ThrowsAsync<TriagemConcurrencyConflictException>(() =>
            repository.SaveTransitionAsync(loserRead, CancellationToken.None));
        Assert.Equal(winnerRead.Id, exception.TriagemId);

        // Recarregar pela chave reflete o vencedor já persistido.
        var reloaded = await repository.FindByKeyAsync(key, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(ETriagemStatus.ContatoIniciado, reloaded!.Status);
        Assert.Equal(winnerRead.ConcurrencyToken, reloaded.ConcurrencyToken);
    }

    [Fact]
    public async Task ConflictsToSimulateOnSaveTransition_ForcaConflitoDeterministicamenteNaProximaChamada()
    {
        var repository = new FakeTriagemRepository { ConflictsToSimulateOnSaveTransition = 1 };
        var key = new TriagemIdempotencyKey(Guid.NewGuid(), Guid.NewGuid(), "OS-1");
        var created = await repository.GetOrAddAsync(key,
            new Triagem(Guid.NewGuid(), key.TenantId, key.IntegrationId, Guid.NewGuid(), Now), CancellationToken.None);
        created.Triagem.MarcarContatoIniciado(Now);

        await Assert.ThrowsAsync<TriagemConcurrencyConflictException>(() =>
            repository.SaveTransitionAsync(created.Triagem, CancellationToken.None));

        // O contador foi consumido — a próxima chamada já não conflita mais.
        await repository.SaveTransitionAsync(created.Triagem, CancellationToken.None);
        var reloaded = await repository.FindByKeyAsync(key, CancellationToken.None);
        Assert.Equal(ETriagemStatus.ContatoIniciado, reloaded!.Status);
    }
}
