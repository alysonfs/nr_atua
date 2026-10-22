using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Expurgo de mídia inbound pendente (ADR-046/047). Elegibilidade é
/// determinada pela aplicação — nunca por S3 Lifecycle — usando o mesmo
/// marco de retenção de <see cref="TriagemInboundMessageRetentionPolicy"/>
/// (ADR-045), aplicado a <see cref="TriagemInboundMedia.ExpiresAtUtc"/>:
/// um item é elegível quando <c>ExpiresAtUtc &lt;= CalculatePurgeThresholdUtc(now)</c>,
/// ou seja, um ano após a expiração de cinco anos desde o recebimento.
///
/// Ordem por item: o objeto em S3 é removido primeiro
/// (<see cref="ITriagemInboundMediaStorage.DeleteAsync"/>, idempotente —
/// objeto já ausente é tratado como sucesso) e só então os metadados são
/// removidos do banco. Uma falha ao excluir o objeto em storage preserva os
/// metadados (o registro permanece elegível e serve de retentativa no
/// próximo ciclo) e o processamento segue para o próximo item do lote —
/// nunca interrompe o lote inteiro por causa de um item com falha. Itens
/// sem <see cref="TriagemInboundMedia.StorageKey"/> (nenhum binário jamais
/// foi armazenado — ex.: <c>SemTriagem</c>, <c>Ambigua</c>, outcomes
/// técnicos) pulam direto para a remoção de metadados.
///
/// Hospedado por <c>TriagemInboundMediaPurgeJob</c> (<c>BackgroundService</c>),
/// mas extraído para uma classe de aplicação testável sem
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> — segue o
/// mesmo padrão de <see cref="TriagemOutboxDrainService"/>.
///
/// S3 Lifecycle não é implementado por este incremento (decisão explícita
/// do ADR-046/047) — esta classe é a única autoridade de exclusão física.
/// </summary>
public sealed class TriagemInboundMediaPurgeService(
    AtuaDbContext dbContext,
    ITriagemInboundMediaStorage storage,
    TimeProvider timeProvider,
    ILogger<TriagemInboundMediaPurgeService> logger)
{
    public const int DefaultBatchSize = 100;

    /// <summary>
    /// Avalia e expurga um único lote (no máximo <paramref name="batchSize"/>
    /// itens), ordenado pelo item mais antigo elegível primeiro. Retorna um
    /// resumo para observabilidade/testes — nunca lança por causa de uma
    /// falha isolada de item (ver classe).
    /// </summary>
    public async Task<TriagemInboundMediaPurgeBatchResult> PurgeOnceAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "O tamanho do lote deve ser positivo.");
        }

        var purgeThresholdUtc =
            TriagemInboundMessageRetentionPolicy.CalculatePurgeThresholdUtc(timeProvider.GetUtcNow());

        var batch = await dbContext.TriagemInboundMedia
            .Where(media => media.ExpiresAtUtc <= purgeThresholdUtc)
            .OrderBy(media => media.ExpiresAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            return TriagemInboundMediaPurgeBatchResult.Empty;
        }

        logger.LogInformation(
            "[TRIAGEM-INBOUND-MEDIA-PURGE] Lote avaliado. BatchSize={BatchSize}", batch.Count);

        var purged = 0;
        var storageFailures = 0;

        foreach (var media in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Itens restantes do lote permanecem intactos — continuam
                // elegíveis e serão reavaliados no próximo ciclo.
                break;
            }

            if (media.StorageKey is not null)
            {
                var key = new TriagemInboundMediaStorageKey(media.TenantId, media.Id);
                try
                {
                    await storage.DeleteAsync(key, cancellationToken);
                }
                catch (TriagemInboundMediaStorageException exception)
                {
                    storageFailures++;
                    logger.LogWarning(exception,
                        "[TRIAGEM-INBOUND-MEDIA-PURGE] Falha ao excluir objeto em storage; " +
                        "metadado mantido para nova tentativa. TenantId={TenantId} MediaId={MediaId}",
                        media.TenantId, media.Id);
                    continue;
                }
            }

            dbContext.TriagemInboundMedia.Remove(media);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                purged++;
                logger.LogInformation(
                    "[TRIAGEM-INBOUND-MEDIA-PURGE] Mídia expurgada. TenantId={TenantId} MediaId={MediaId}",
                    media.TenantId, media.Id);
            }
            catch (DbUpdateException exception)
            {
                // Objeto em storage já removido (ou nunca existiu); registro
                // de metadados volta a ser avaliado no próximo ciclo —
                // DeleteAsync é idempotente, então reexcluir o mesmo objeto
                // em S3 é inofensivo.
                dbContext.ChangeTracker.Clear();
                storageFailures++;
                logger.LogError(exception,
                    "[TRIAGEM-INBOUND-MEDIA-PURGE] Falha ao excluir metadado após excluir objeto em storage. " +
                    "TenantId={TenantId} MediaId={MediaId}",
                    media.TenantId, media.Id);
            }
        }

        return new TriagemInboundMediaPurgeBatchResult(batch.Count, purged, storageFailures);
    }
}

/// <summary>Resumo de um ciclo de expurgo — observabilidade/testes.</summary>
public sealed record TriagemInboundMediaPurgeBatchResult(int BatchSize, int Purged, int StorageFailures)
{
    public static readonly TriagemInboundMediaPurgeBatchResult Empty = new(0, 0, 0);
}
