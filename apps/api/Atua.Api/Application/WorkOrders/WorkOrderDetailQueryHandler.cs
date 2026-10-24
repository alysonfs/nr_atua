using Atua.Api.Application.LlmUsage;
using Atua.Api.Application.LlmUsage.Contracts;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atua.Api.Application.WorkOrders;

/// <summary>
/// RF-026.5/RF-026.6: detalhe completo de uma OS, com histórico de status,
/// para a página de detalhes de OS no Office.
/// </summary>
public sealed class WorkOrderDetailQueryHandler(
    AtuaDbContext dbContext,
    ITriagemLlmUsageByTriagemQuery llmUsageByTriagemQuery,
    TriagemLlmPricingCatalog pricingCatalog,
    IOptions<TriagemLlmConversationOptions> conversationOptions,
    TimeProvider timeProvider) : IWorkOrderDetailQuery
{
    public async Task<WorkOrderDetailResult?> ExecuteAsync(Guid tenantId, Guid workOrderId,
        CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.WorkOrders.AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == workOrderId && order.TenantId == tenantId,
                cancellationToken);
        if (workOrder is null) return null;

        var history = await dbContext.WorkOrderHistories.AsNoTracking()
            .Where(entry => entry.WorkOrderId == workOrderId && entry.TenantId == tenantId)
            .OrderBy(entry => entry.CreatedAt)
            .Select(entry => new WorkOrderHistoryEntryResult(entry.Status, entry.CreatedAt))
            .ToListAsync(cancellationToken);

        var triagem = await dbContext.Triagens.AsNoTracking()
            .Where(triagem => triagem.WorkOrderId == workOrderId && triagem.TenantId == tenantId)
            .OrderByDescending(triagem => triagem.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        TriagemLlmCostSummary llmUsage = TriagemLlmCostSummary.Zero;
        WarrantyEstimate warrantyEstimate = WarrantyEstimateCalculator.Calculate(
            null, conversationOptions.Value.GarantiaPadraoMeses, timeProvider.GetUtcNow());

        if (triagem is not null)
        {
            var llmUsageRows = await llmUsageByTriagemQuery.ExecuteAsync([triagem.Id], cancellationToken);
            llmUsage = TriagemLlmCostSummaryCalculator.Build(llmUsageRows, pricingCatalog);

            warrantyEstimate = WarrantyEstimateCalculator.Calculate(
                triagem.DataEmissaoNotaFiscal, conversationOptions.Value.GarantiaPadraoMeses,
                timeProvider.GetUtcNow());
        }

        return new WorkOrderDetailResult(
            workOrder.Id,
            workOrder.WorkOrderProviderId,
            workOrder.WorkOrderProviderNo,
            workOrder.Status,
            workOrder.CreatedAt,
            workOrder.UpdatedAt,
            workOrder.ProviderCreatedAt,
            workOrder.ProviderUpdatedAt,
            workOrder.ServiceRequestId,
            workOrder.Amount,
            workOrder.CustomerType,
            workOrder.CustomerName,
            workOrder.CustomerCpf,
            workOrder.ContactEmail,
            workOrder.ContactPhone,
            workOrder.ContactName,
            workOrder.Address,
            workOrder.ZipCode,
            workOrder.CountryName,
            workOrder.StateName,
            workOrder.CityName,
            workOrder.ProductBrand,
            workOrder.PdCode,
            workOrder.CategoryId,
            workOrder.ProductCategoryCode,
            workOrder.ProductCode,
            workOrder.ProductModel,
            workOrder.ProductStatus,
            workOrder.Symptom,
            workOrder.SymptomRaw,
            history,
            triagem?.Id,
            triagem?.CreatedAtUtc,
            triagem?.ColetaEncerradaEmUtc,
            warrantyEstimate,
            triagem?.NumeroNotaFiscal,
            triagem?.SerieNotaFiscal,
            llmUsage);
    }
}
