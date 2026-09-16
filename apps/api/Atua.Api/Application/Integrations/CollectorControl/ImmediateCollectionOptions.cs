namespace Atua.Api.Application.Integrations.CollectorControl;

/// <summary>
/// Configurações do comando de coleta imediata (ADR-021/D1+D6).
///
/// <c>HistoryWindowMonths</c>: janela temporal de busca de OS (últimos N
/// meses a partir da execução do comando). Configurável para não hardcodar
/// o limite de 3 meses (ADR-021/D1+D6).
///
/// Valores default são conservadores para o MVP; ajustáveis via appsettings
/// ou variável de ambiente sem redeploy.
/// </summary>
public sealed class ImmediateCollectionOptions
{
    public const string SectionName = "ImmediateCollection";

    /// <summary>
    /// Janela temporal de histórico para a coleta inicial (meses).
    /// ADR-021/D1+D6: limite absoluto de 3 meses, mas configurável. Reduzido
    /// para 1 mês (decisão operacional) após ciclo real com 3 meses/200-por-
    /// página ter devolvido volume (1035 OS) que o consumer da Fase 4 não
    /// conseguiu processar por completo no mesmo ciclo — ver validação de
    /// Fase 4 em <c>PLANO-refactor-provider-interactions-collector.md</c>.
    /// </summary>
    public int HistoryWindowMonths { get; init; } = 1;

    /// <summary>
    /// Timeout do claim antes de o comando transitar para Failed/ClaimTimeout
    /// (ADR-021/D2: 30 minutos).
    /// </summary>
    public int ClaimTimeoutMinutes { get; init; } = 30;
}
