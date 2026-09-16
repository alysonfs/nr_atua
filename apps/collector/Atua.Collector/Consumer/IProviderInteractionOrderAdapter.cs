using MongoDB.Bson;

namespace Atua.Collector.Consumer;

/// <summary>
/// Extrai as OS individuais do campo <c>orders</c> de um documento de
/// <c>provider_interactions</c> (Fase 4 do refactor de persistência —
/// docs/implementation/PLANO-refactor-provider-interactions-collector.md).
/// Uma implementação por provedor — selecionada pelo consumer via <c>provider_type</c>.
/// Substitui <see cref="ISnapshotAdapter"/>, que extraía o status de uma única OS por
/// documento (modelo de <c>work_order_snapshots</c>, superseded).
/// </summary>
public interface IProviderInteractionOrderAdapter
{
    /// <summary>
    /// Extrai os dados de cada OS presente em <paramref name="orders"/>, incluindo
    /// identificador, status e os campos descritivos de consumidor/contato/endereço/produto
    /// (ver <see cref="ProviderWorkOrderData"/>). OS sem identificador ou sem status
    /// utilizável são omitidas do resultado — o consumer loga um warning por OS descartada,
    /// sem abortar o restante do documento.
    /// </summary>
    IReadOnlyList<ProviderWorkOrderData> ExtractOrders(BsonArray orders);
}
