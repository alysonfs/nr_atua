namespace Atua.Channel.Configuration;

/// <summary>
/// Configuração do processo do canal WhatsApp (ADR-042). Nenhum valor
/// sensível tem padrão não vazio — tokens devem ser fornecidos por
/// variável de ambiente ou secrets do ambiente de execução, nunca
/// hardcoded no código-fonte ou em <c>appsettings.json</c> versionado
/// (apenas placeholders são versionados).
/// </summary>
public sealed class ChannelWorkerOptions
{
    public const string SectionName = "ChannelWorker";

    /// <summary>URL base da API Atua (ex.: https://api.atua.internal).</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Token opaco de <c>ServiceCredential</c> com escopo exclusivo
    /// <c>channel.command.claim</c>. Configure via variável de ambiente ou
    /// secrets — nunca hardcode.
    /// </summary>
    public string ClaimServiceToken { get; set; } = string.Empty;

    /// <summary>
    /// Token opaco de <c>ServiceCredential</c> com escopo exclusivo
    /// <c>channel.command.complete</c>. Configure via variável de ambiente
    /// ou secrets — nunca hardcode.
    /// </summary>
    public string CompleteServiceToken { get; set; } = string.Empty;

    /// <summary>
    /// Intervalo de polling (segundos) entre tentativas de <c>claim</c> —
    /// no máximo 30 segundos (ADR-042; arquitetura, seção 2.4). Validado em
    /// <c>Program.cs</c> na inicialização.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 15;
}
