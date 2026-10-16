using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atua.Channel.Configuration;
using Atua.Channel.Contracts;
using Microsoft.Extensions.Options;

namespace Atua.Channel.Api;

/// <summary>
/// Implementação de <see cref="IChannelApiClient"/> sobre os endpoints
/// internos de <c>claim</c>/<c>complete</c> do canal (ADR-042). Usa
/// <c>HttpClient</c> tipado registrado via <c>AddHttpClient</c>. Nunca loga
/// corpo de requisição/resposta — o payload de claim carrega chave de
/// idempotência e identificadores internos; não é sensível por si, mas o
/// padrão do projeto evita logar bodies por princípio (mesmo critério do
/// Agente Coletor).
/// </summary>
public sealed class ChannelApiClient(
    HttpClient httpClient,
    IOptions<ChannelWorkerOptions> options,
    ILogger<ChannelApiClient> logger)
    : IChannelApiClient
{
    private readonly ChannelWorkerOptions _options = options.Value;

    /// <inheritdoc/>
    public async Task<ClaimChannelCommandResponse?> ClaimAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/triagens/canal/claim");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ClaimServiceToken);

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
            logger.LogDebug("[CLAIM] Nenhum comando de canal disponível (204).");
            return null;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ClaimChannelCommandResponse>(cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("[CLAIM] A API retornou 200 mas o body não pôde ser desserializado.");
        }

        logger.LogInformation(
            "[CLAIM] Comando de canal {CommandId} reivindicado. TriagemId={TriagemId} AttemptCount={AttemptCount}.",
            result.CommandId, result.TriagemId, result.AttemptCount);

        return result;
    }

    /// <inheritdoc/>
    public async Task<CompleteChannelCommandResponse?> CompleteAsync(
        Guid commandId,
        DateTimeOffset claimedAtUtc,
        string outcome,
        string? errorCode,
        string? errorDescription,
        CancellationToken cancellationToken = default)
    {
        var body = new CompleteChannelCommandRequest(commandId, claimedAtUtc, outcome, errorCode, errorDescription);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/triagens/canal/complete")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.CompleteServiceToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[COMPLETE] Falha de comunicação ao chamar /complete para CommandId={CommandId}.",
                commandId);
            throw;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // Lease perdida (ADR-042, correção de concorrência): o token
            // ClaimedAtUtc apresentado não é mais o vigente — a Master API
            // não aplicou nenhum efeito. Nunca trata como erro de transporte.
            logger.LogWarning(
                "[COMPLETE] Lease perdida para CommandId={CommandId} (409) — nenhum efeito foi aplicado.",
                commandId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CompleteChannelCommandResponse>(cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException(
                $"[COMPLETE] A API retornou 200 mas o body não pôde ser desserializado. CommandId={commandId}.");
        }

        logger.LogInformation(
            "[COMPLETE] Comando de canal {CommandId} concluído. Outcome={Outcome} Status={Status}.",
            commandId, outcome, result.Status);

        return result;
    }
}
