namespace Atua.Collector.Configuration;

public sealed class CollectorWorkerOptions
{
    public const string SectionName = "CollectorWorker";

    /// <summary>URL base da API Atua (ex: https://api.atua.internal).</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Token opaco de ServiceCredential com escopo <c>collector.command.claim</c>.
    /// Configure via variável de ambiente ou secrets — nunca hardcode.
    /// </summary>
    public string ClaimServiceToken { get; set; } = string.Empty;

    /// <summary>
    /// Token opaco de ServiceCredential com escopo <c>collector.command.complete</c>.
    /// Configure via variável de ambiente ou secrets — nunca hardcode.
    /// </summary>
    public string CompleteServiceToken { get; set; } = string.Empty;

    /// <summary>
    /// Token opaco de ServiceCredential com escopo <c>collector.eligibility.read</c>.
    /// Configure via variável de ambiente ou secrets — nunca hardcode.
    /// </summary>
    public string EligibilityServiceToken { get; set; } = string.Empty;

    /// <summary>Intervalo de espera (segundos) quando o claim retorna 204 (sem comando disponível).</summary>
    public int PollingIntervalSeconds { get; set; } = 30;

    /// <summary>Executar o browser Playwright em modo headless.</summary>
    public bool Headless { get; set; } = true;
}
