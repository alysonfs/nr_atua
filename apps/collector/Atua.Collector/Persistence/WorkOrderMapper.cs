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
    /// // TODO(D7): implementação provisória usa <c>workOrderNo</c>.
    /// Após descoberta do iService real (ADR-021/D7), avaliar troca para
    /// <c>workOrderId</c> e atualizar o índice de idempotência no MongoDB.
    /// Trocar SOMENTE aqui — nenhuma outra referência deve existir.
    /// </summary>
    /// <param name="rawOrder">Dicionário com os campos brutos da OS do iService.</param>
    /// <returns>
    /// O valor de <c>workOrderNo</c> como string, ou <c>null</c> se ausente/nulo.
    /// </returns>
    public static string? ExtractProviderOrderId(IDictionary<string, object?> rawOrder)
    {
        // TODO(D7): campo provisório — usar workOrderNo até definição final de D7.
        if (rawOrder.TryGetValue("workOrderNo", out var value) && value is not null)
            return value.ToString();

        return null;
    }
}
