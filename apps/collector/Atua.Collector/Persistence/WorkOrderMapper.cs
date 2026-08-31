namespace Atua.Collector.Persistence;

/// <summary>
/// Mapeia campos do iService para o modelo interno do ATUA.
///
/// ISOLAMENTO D7 (ADR-021): este é o único componente que deve conhecer
/// o nome do campo do iService usado como <c>providerOrderId</c>.
/// Nenhuma outra parte do código deve referenciar diretamente
/// <c>workOrderNo</c> ou <c>workOrderId</c> do payload do iService.
/// </summary>
public static class WorkOrderMapper
{
    /// <summary>
    /// Extrai o <c>providerOrderId</c> de um registro bruto do iService.
    ///
    /// D7 (resolvido): usa <c>workOrderId</c> — chave interna numérica do
    /// iService, validada como estável em ~100 capturas reais de produção
    /// (25-26/08/2026, 23 OS distintas, zero instabilidade no par
    /// workOrderId↔workOrderNo). Preferido a <c>workOrderNo</c> por ser a
    /// PK do sistema legado; <c>workOrderNo</c> é o número de documento
    /// derivado, sujeito a regras de numeração administrativa.
    /// Trocar SOMENTE aqui — nenhuma outra referência deve existir.
    /// </summary>
    /// <param name="rawOrder">Dicionário com os campos brutos da OS do iService.</param>
    /// <returns>
    /// O valor de <c>workOrderId</c> como string, ou <c>null</c> se ausente/nulo.
    /// </returns>
    public static string? ExtractProviderOrderId(IDictionary<string, object?> rawOrder)
    {
        if (rawOrder.TryGetValue("workOrderId", out var value) && value is not null)
            return value.ToString();

        return null;
    }
}
