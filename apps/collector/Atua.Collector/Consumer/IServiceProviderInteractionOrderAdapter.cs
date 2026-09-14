using MongoDB.Bson;

namespace Atua.Collector.Consumer;

/// <summary>
/// Adaptador de <c>provider_interactions</c> para o provedor iService (Fase 4 do refactor de
/// persistência). Cada OS do array <c>orders</c> é um <see cref="BsonDocument"/> com os
/// mesmos campos brutos devolvidos pelo iService: identificador em <c>workOrderId</c> (ou,
/// no fallback usado pelo enriquecimento de detalhe, <c>id</c>) e status em <c>woStatus</c>.
/// </summary>
public sealed class IServiceProviderInteractionOrderAdapter : IProviderInteractionOrderAdapter
{
    /// <inheritdoc/>
    public IReadOnlyList<(string ProviderId, string Status)> ExtractOrders(BsonArray orders)
    {
        var result = new List<(string ProviderId, string Status)>(orders.Count);

        foreach (var item in orders)
        {
            if (item is not BsonDocument order) continue;

            var providerId = ExtractProviderId(order);
            var status = ExtractStatus(order);

            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(status))
                continue;

            result.Add((providerId, status));
        }

        return result;
    }

    private static string? ExtractProviderId(BsonDocument order)
    {
        if (order.TryGetValue("workOrderId", out var workOrderId) && !workOrderId.IsBsonNull)
            return workOrderId.ToString();

        return order.TryGetValue("id", out var id) && !id.IsBsonNull ? id.ToString() : null;
    }

    private static string? ExtractStatus(BsonDocument order)
    {
        if (!order.TryGetValue("woStatus", out var value) || value.IsBsonNull)
            return null;

        var status = value.ToString();
        return string.IsNullOrWhiteSpace(status) ? null : status;
    }
}
