namespace Atua.Collector.Consumer;

/// <summary>
/// Dados de uma OS extraídos de um documento de <c>provider_interactions</c> — substitui a
/// tupla <c>(string ProviderId, string Status)</c> usada até a Fase 4, agora carregando também
/// datas do provedor e os campos de consumidor/contato/endereço/produto/sintoma necessários
/// para a triagem e obtenção de peças (ver
/// docs/implementation/PLANO-refactor-provider-interactions-collector.md).
/// Todos os campos além de <see cref="ProviderId"/>/<see cref="Status"/> são opcionais — o
/// provedor pode omiti-los ou devolvê-los vazios.
/// </summary>
public sealed record ProviderWorkOrderData(
    string ProviderId,
    string Status,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderUpdatedAt,
    string? CustomerType,
    string? CustomerName,
    string? CustomerCpf,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactName,
    string? Address,
    string? ZipCode,
    string? CountryName,
    string? StateName,
    string? CityName,
    string? ProductBrand,
    string? PdCode,
    string? CategoryId,
    string? ProductCategoryCode,
    string? ProductCode,
    string? ProductModel,
    string? ProductStatus,
    string? Symptom);
