using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de <see cref="TriagemInboundMediaPurgeService"/> (ADR-046/047) —
/// expurgo de mídia inbound pendente. Usa o provedor EF Core InMemory (mesmo
/// padrão de <see cref="RecurrentCollectionSchedulerServiceTests"/>) e
/// <see cref="FakeTriagemInboundMediaStorage"/> — nenhum teste aqui faz
/// qualquer chamada à AWS.
/// </summary>
public class TriagemInboundMediaPurgeServiceTests
{
    private static readonly DateTimeOffset ReceivedAtUtc = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ExpiresAtUtc = ReceivedAtUtc + 5 anos = 2025-01-01. Elegível quando
    // ExpiresAtUtc + 1 ano <= now, ou seja, now >= 2026-01-01.
    private static readonly DateTimeOffset EligibleNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NotYetEligibleNow = new(2025, 12, 31, 23, 59, 59, TimeSpan.Zero);

    [Fact]
    public async Task MidiaAindaNaoElegivel_NaoEExpurgada()
    {
        await using var context = CreateContext();
        var storage = new FakeTriagemInboundMediaStorage();
        var media = SeedArmazenada(context, storage);
        var service = CreateService(context, storage, NotYetEligibleNow);

        var result = await service.PurgeOnceAsync(10, CancellationToken.None);

        Assert.Equal(TriagemInboundMediaPurgeBatchResult.Empty, result);
        Assert.NotNull(await context.TriagemInboundMedia.FindAsync(media.Id));
        Assert.True(storage.Objects.ContainsKey(new TriagemInboundMediaStorageKey(media.TenantId, media.Id).Value));
    }

    [Fact]
    public async Task MidiaElegivelComObjetoEmStorage_ApagaObjetoDepoisMetadado()
    {
        await using var context = CreateContext();
        var storage = new FakeTriagemInboundMediaStorage();
        var media = SeedArmazenada(context, storage);
        var key = new TriagemInboundMediaStorageKey(media.TenantId, media.Id).Value;
        var service = CreateService(context, storage, EligibleNow);

        var result = await service.PurgeOnceAsync(10, CancellationToken.None);

        Assert.Equal(new TriagemInboundMediaPurgeBatchResult(1, 1, 0), result);
        Assert.Null(await context.TriagemInboundMedia.FindAsync(media.Id));
        Assert.False(storage.Objects.ContainsKey(key));
    }

    [Fact]
    public async Task MidiaElegivelSemStorageKey_ApagaApenasMetadado()
    {
        // Outcomes técnicos (ex.: SemTriagem/Ambigua) nunca armazenam
        // binário — StorageKey é nulo, então o expurgo pula direto para a
        // remoção de metadados, sem chamar o storage.
        await using var context = CreateContext();
        var storage = new FakeTriagemInboundMediaStorage();
        var media = NewMedia(ReceivedAtUtc, ETriagemInboundMediaOutcome.SemTriagem, storageKey: null);
        context.TriagemInboundMedia.Add(media);
        await context.SaveChangesAsync();
        var service = CreateService(context, storage, EligibleNow);

        var result = await service.PurgeOnceAsync(10, CancellationToken.None);

        Assert.Equal(new TriagemInboundMediaPurgeBatchResult(1, 1, 0), result);
        Assert.Null(await context.TriagemInboundMedia.FindAsync(media.Id));
    }

    [Fact]
    public async Task FalhaAoExcluirObjetoEmStorage_MantemMetadadoESeguePraProximoItem()
    {
        await using var context = CreateContext();
        var storage = new FakeTriagemInboundMediaStorage();
        var falha = SeedArmazenada(context, storage);
        var ok = SeedArmazenada(context, storage);
        storage.FailDeleteForKeys.Add(new TriagemInboundMediaStorageKey(falha.TenantId, falha.Id).Value);
        var service = CreateService(context, storage, EligibleNow);

        var result = await service.PurgeOnceAsync(10, CancellationToken.None);

        Assert.Equal(new TriagemInboundMediaPurgeBatchResult(2, 1, 1), result);
        // Item com falha: metadado preservado (retry no próximo ciclo) e
        // objeto nunca removido do storage.
        Assert.NotNull(await context.TriagemInboundMedia.FindAsync(falha.Id));
        Assert.True(storage.Objects.ContainsKey(new TriagemInboundMediaStorageKey(falha.TenantId, falha.Id).Value));
        // Item sem falha: processado normalmente mesmo após a falha do item
        // anterior do mesmo lote.
        Assert.Null(await context.TriagemInboundMedia.FindAsync(ok.Id));
    }

    [Fact]
    public async Task Paginacao_RespeitaTamanhoDoLote()
    {
        await using var context = CreateContext();
        var storage = new FakeTriagemInboundMediaStorage();
        for (var i = 0; i < 5; i++)
        {
            SeedArmazenada(context, storage);
        }
        var service = CreateService(context, storage, EligibleNow);

        var firstBatch = await service.PurgeOnceAsync(2, CancellationToken.None);
        Assert.Equal(new TriagemInboundMediaPurgeBatchResult(2, 2, 0), firstBatch);

        var secondBatch = await service.PurgeOnceAsync(2, CancellationToken.None);
        Assert.Equal(new TriagemInboundMediaPurgeBatchResult(2, 2, 0), secondBatch);

        var thirdBatch = await service.PurgeOnceAsync(2, CancellationToken.None);
        Assert.Equal(new TriagemInboundMediaPurgeBatchResult(1, 1, 0), thirdBatch);

        var fourthBatch = await service.PurgeOnceAsync(2, CancellationToken.None);
        Assert.Equal(TriagemInboundMediaPurgeBatchResult.Empty, fourthBatch);
    }

    private static TriagemInboundMediaPurgeService CreateService(
        AtuaDbContext context, FakeTriagemInboundMediaStorage storage, DateTimeOffset now) =>
        new(context, storage, new FakeTimeProvider(now), NullLogger<TriagemInboundMediaPurgeService>.Instance);

    private static TriagemInboundMedia SeedArmazenada(AtuaDbContext context, FakeTriagemInboundMediaStorage storage)
    {
        var mediaId = Guid.CreateVersion7();
        var tenantId = Guid.NewGuid();
        var key = new TriagemInboundMediaStorageKey(tenantId, mediaId);
        storage.PutIfAbsentAsync(
            key, new MemoryStream([1, 2, 3]), "application/pdf", 3, CancellationToken.None).GetAwaiter().GetResult();

        var media = NewMedia(ReceivedAtUtc, ETriagemInboundMediaOutcome.Armazenada, key.Value, mediaId, tenantId);
        context.TriagemInboundMedia.Add(media);
        context.SaveChanges();
        return media;
    }

    private static TriagemInboundMedia NewMedia(
        DateTimeOffset receivedAtUtc,
        ETriagemInboundMediaOutcome outcome,
        string? storageKey,
        Guid? id = null,
        Guid? tenantId = null) =>
        new(
            id ?? Guid.CreateVersion7(),
            tenantId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            $"channel-message-{Guid.NewGuid()}",
            $"idempotency-{Guid.NewGuid()}",
            Guid.NewGuid(),
            "5511999991234",
            "direct",
            false,
            receivedAtUtc,
            ETriagemInboundMediaKind.Document,
            "application/pdf",
            3,
            sha256: null,
            isVoiceNote: false,
            durationSeconds: null,
            caption: null,
            outcome,
            triagemId: null,
            candidateTriagemIds: [],
            storageKey,
            createdAtUtc: receivedAtUtc);

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
