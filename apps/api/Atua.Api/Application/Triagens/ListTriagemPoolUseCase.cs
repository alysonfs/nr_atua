using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação de <see cref="IListTriagemPoolUseCase"/> sobre o PostgreSQL
/// da Master API (mesmo padrão de leitura dos demais query handlers — ver
/// <c>Atua.Api.Application.WorkOrders.WorkOrderListByStatusQueryHandler</c>).
/// </summary>
public sealed class ListTriagemPoolUseCase(AtuaDbContext dbContext) : IListTriagemPoolUseCase
{
    public async Task<TriagemPoolListResult> ExecuteAsync(TriagemPoolListQuery query,
        CancellationToken cancellationToken)
    {
        var isMember = await dbContext.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == query.RequestingUserId && membership.TenantId == query.TenantId,
            cancellationToken);
        if (!isMember) return TriagemPoolListResult.Failure(ETriagemPoolListOutcome.Forbidden);

        if (query.StatusFilter is not null && !Triagem.PoolStatuses.Contains(query.StatusFilter.Value))
        {
            return TriagemPoolListResult.Failure(ETriagemPoolListOutcome.InvalidStatusFilter);
        }

        var statuses = query.StatusFilter is null
            ? Triagem.PoolStatuses
            : [query.StatusFilter.Value];

        var rows = await dbContext.Triagens.AsNoTracking()
            .Where(triagem => triagem.TenantId == query.TenantId && statuses.Contains(triagem.Status))
            .OrderBy(triagem => triagem.UpdatedAtUtc)
            .Select(triagem => new
            {
                triagem.Id,
                triagem.WorkOrderId,
                triagem.Status,
                triagem.UpdatedAtUtc,
                ChecklistItemsCompleted =
                    (triagem.DescricaoDefeito != null && triagem.DescricaoDefeito != "" ? 1 : 0)
                    + (triagem.NotaFiscalLegivel ? 1 : 0)
                    + (triagem.EtiquetaModelo != null && triagem.EtiquetaModelo != ""
                        && triagem.EtiquetaSerie != null && triagem.EtiquetaSerie != "" ? 1 : 0)
                    + (triagem.MidiaDefeitoTipo != null ? 1 : 0)
                    + (triagem.EnderecoCompleto != null && triagem.EnderecoCompleto != "" ? 1 : 0),
            })
            .ToListAsync(cancellationToken);

        var triagemIds = rows.Select(row => row.Id).ToArray();
        var workOrderIds = rows.Select(row => row.WorkOrderId).ToArray();

        var workOrders = await dbContext.WorkOrders.AsNoTracking()
            .Where(order => workOrderIds.Contains(order.Id))
            .Select(order => new { order.Id, order.WorkOrderProviderNo, order.CustomerName, order.ProductModel,
                order.ProductBrand })
            .ToDictionaryAsync(order => order.Id, cancellationToken);

        var lastActivityByTriagemId = await GetLastConsumerActivityAsync(triagemIds, cancellationToken);

        var items = rows.Select(row =>
        {
            workOrders.TryGetValue(row.WorkOrderId, out var workOrder);
            lastActivityByTriagemId.TryGetValue(row.Id, out var lastActivityAtUtc);
            var hasNewActivity = lastActivityAtUtc != default && lastActivityAtUtc > row.UpdatedAtUtc;

            return new TriagemPoolListItem(
                row.Id,
                workOrder?.WorkOrderProviderNo,
                workOrder?.CustomerName,
                workOrder?.ProductModel,
                workOrder?.ProductBrand,
                row.Status,
                row.ChecklistItemsCompleted,
                Triagem.ChecklistItensMinimosTotal,
                row.UpdatedAtUtc,
                hasNewActivity);
        }).ToArray();

        return new TriagemPoolListResult(ETriagemPoolListOutcome.Success, items.Length, items);
    }

    /// <summary>
    /// Último momento de atividade do consumidor (mensagem de texto ou
    /// mídia) por triagem — usado para o indicador de "nova atividade"
    /// (critério de aceite 10). Mensagens/mídias vinculadas a uma triagem
    /// (<c>TriagemId</c> não nulo) são sempre do consumidor: eventos
    /// próprios (<c>fromMe</c>), de grupo ou vazios nunca recebem vínculo
    /// (ver <c>TriagemInboundMessageRepository</c>/<c>TriagemInboundMediaRepository</c>).
    /// </summary>
    private async Task<Dictionary<Guid, DateTimeOffset>> GetLastConsumerActivityAsync(
        IReadOnlyCollection<Guid> triagemIds, CancellationToken cancellationToken)
    {
        if (triagemIds.Count == 0) return [];

        var lastMessageByTriagemId = await dbContext.TriagemInboundMessages.AsNoTracking()
            .Where(message => message.TriagemId != null && triagemIds.Contains(message.TriagemId.Value))
            .GroupBy(message => message.TriagemId!.Value)
            .Select(group => new { TriagemId = group.Key, LastAtUtc = group.Max(m => m.ReceivedAtUtc) })
            .ToDictionaryAsync(entry => entry.TriagemId, entry => entry.LastAtUtc, cancellationToken);

        var lastMediaByTriagemId = await dbContext.TriagemInboundMedia.AsNoTracking()
            .Where(media => media.TriagemId != null && triagemIds.Contains(media.TriagemId.Value))
            .GroupBy(media => media.TriagemId!.Value)
            .Select(group => new { TriagemId = group.Key, LastAtUtc = group.Max(m => m.ReceivedAtUtc) })
            .ToDictionaryAsync(entry => entry.TriagemId, entry => entry.LastAtUtc, cancellationToken);

        var merged = new Dictionary<Guid, DateTimeOffset>(lastMessageByTriagemId);
        foreach (var (triagemId, lastAtUtc) in lastMediaByTriagemId)
        {
            if (!merged.TryGetValue(triagemId, out var current) || lastAtUtc > current)
            {
                merged[triagemId] = lastAtUtc;
            }
        }

        return merged;
    }
}
