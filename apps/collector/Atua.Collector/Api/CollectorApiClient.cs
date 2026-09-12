using System.Net;
using System.Net.Http.Json;
using Atua.Collector.Configuration;
using Atua.Collector.Contracts;
using Atua.Collector.Diagnostics;
using Microsoft.Extensions.Options;

namespace Atua.Collector.Api;

/// <summary>
/// Implementação do cliente HTTP para os endpoints internos do Coletor (RF-009).
/// Utiliza HttpClient tipado registrado via <c>AddHttpClient</c>.
/// </summary>
public sealed class CollectorApiClient(
    HttpClient httpClient,
    IOptions<CollectorWorkerOptions> options,
    ILogger<CollectorApiClient> logger,
    IIServiceDebugLogger debugLogger)
    : ICollectorApiClient
{
    private readonly CollectorWorkerOptions _options = options.Value;

    /// <inheritdoc/>
    public async Task<ClaimResponse?> ClaimAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/internal/collector/commands/claim");

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ClaimServiceToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[CLAIM] Falha de comunicação ao chamar /claim.");
            throw;
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            logger.LogDebug("[CLAIM] Nenhum comando disponível (204).");
            return null;
        }

        response.EnsureSuccessStatusCode();

        // ADR-021/D9-B: não logar o body — contém credenciais em claro.
        var result = await response.Content.ReadFromJsonAsync<ClaimResponse>(cancellationToken);
        if (result is null)
            throw new InvalidOperationException("[CLAIM] A API retornou 200 mas o body não pôde ser desserializado.");

        logger.LogInformation(
            "[CLAIM] Comando {CommandId} reivindicado. IntegrationId={IntegrationId} Username={Username}",
            result.CommandId,
            result.IntegrationId,
            result.Credential.Username);

        // Inicia o ciclo de log de diagnóstico local (no-op quando desabilitado) — a
        // partir daqui, Worker/IServiceCollectorService escrevem no mesmo arquivo.
        debugLogger.BeginCycle(result.CommandId, result.TenantId, result.Credential.Username);
        debugLogger.LogApiCall(
            result.CommandId,
            "ClaimAsync",
            "POST",
            "/api/internal/collector/commands/claim",
            (int)response.StatusCode,
            requestBody: null,
            // Password/BaseUrl são mascarados por SensitiveDataRedactor antes da escrita.
            responseBody: result);

        return result;
    }

    /// <inheritdoc/>
    public async Task<EligibilityResponse> CheckEligibilityAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/internal/collector/eligibility");

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.EligibilityServiceToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[ELIGIBILITY] Falha de comunicação ao chamar /eligibility.");
            throw;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EligibilityResponse>(cancellationToken);
        if (result is null)
            throw new InvalidOperationException("[ELIGIBILITY] A API retornou 200 mas o body não pôde ser desserializado.");

        logger.LogInformation(
            "[ELIGIBILITY] Elegibilidade verificada: Eligible={Eligible} EvaluatedAtUtc={EvaluatedAtUtc}.",
            result.Eligible,
            result.EvaluatedAtUtc);

        return result;
    }

    /// <inheritdoc/>
    public async Task<CompleteResponse> CompleteAsync(
        Guid commandId,
        string outcome,
        ECommandFailureReason? failureReason,
        DateTimeOffset? completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var body = new CompleteRequest(
            outcome,
            failureReason?.ToString(),
            completedAtUtc ?? DateTimeOffset.UtcNow);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/internal/collector/commands/{commandId}/complete")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.CompleteServiceToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[COMPLETE] Falha de comunicação ao chamar /complete para CommandId={CommandId}.", commandId);
            throw;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CompleteResponse>(cancellationToken);
        if (result is null)
            throw new InvalidOperationException($"[COMPLETE] A API retornou 200 mas o body não pôde ser desserializado. CommandId={commandId}");

        logger.LogInformation(
            "[COMPLETE] Comando {CommandId} concluído com outcome={Outcome} status={Status}.",
            commandId,
            outcome,
            result.Status);

        debugLogger.LogApiCall(
            commandId,
            "CompleteAsync",
            "POST",
            $"/api/internal/collector/commands/{commandId}/complete",
            (int)response.StatusCode,
            requestBody: body,
            responseBody: result);

        // Encerra o ciclo de log de diagnóstico local iniciado em ClaimAsync (no-op
        // quando desabilitado).
        debugLogger.EndCycle(commandId);

        return result;
    }
}
