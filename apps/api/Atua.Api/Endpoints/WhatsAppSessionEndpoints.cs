using System.Security.Claims;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Atua.Api.Endpoints;

/// <summary>
/// Pareamento via QR (ADR-049): a cada <c>make down</c> a EC2 e seu EBS são
/// destruídos, a sessão Baileys se perde e o tenant precisa ler o QR de
/// novo. O Channel reporta o estado da sessão a
/// <c>POST /api/internal/channel/session</c> (credencial de serviço,
/// escopo exclusivo <c>channel.session.report</c>); o Office consulta o
/// último estado em <c>GET /api/tenants/{tenantId}/whatsapp-session</c>
/// (sessão de navegador, restrito a OWNER/ADMIN do tenant).
///
/// O conteúdo do QR nunca é logado — apenas armazenado em memória (TTL de
/// ~60s) e devolvido ao Office enquanto válido.
/// </summary>
public static class WhatsAppSessionEndpoints
{
    private static readonly string[] ValidStates = ["qr_pending", "connected", "disconnected", "logged_out"];

    public static void MapWhatsAppSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/internal/channel/session", ReportAsync)
            .RequireAuthorization("ChannelSessionReport")
            .WithName("ReportWhatsAppSessionState")
            .WithSummary("Reporta o estado de pareamento da sessão Baileys do Channel (ADR-049).")
            .WithDescription(
                "Guarda apenas o último estado por tenant+integração (claims da credencial de serviço " +
                "autenticada). O QR expira ~60s após o instante reportado e deixa de ser devolvido ao " +
                "Office. Nunca loga o conteúdo do QR.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        endpoints.MapGet("/api/tenants/{tenantId:guid}/whatsapp-session", GetAsync)
            .RequireAuthorization("BrowserSession")
            .WithName("GetWhatsAppSessionState")
            .WithSummary("Consulta o último estado de pareamento WhatsApp do tenant (ADR-049).")
            .WithDescription(
                "Restrito a membros OWNER/ADMIN do tenant. Sem estado conhecido, devolve state=\"unknown\". " +
                "O QR só é devolvido quando o estado é \"qr_pending\" e ainda não expirou. Quando o tenant " +
                "tem mais de uma integração WhatsApp, informe integrationId explicitamente.")
            .Produces<WhatsAppSessionStateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    // -----------------------------------------------------------------------
    // Report (Channel -> API)
    // -----------------------------------------------------------------------

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> ReportAsync(
        ReportWhatsAppSessionStateRequest request,
        ClaimsPrincipal user,
        IReportWhatsAppSessionStateUseCase useCase,
        CancellationToken cancellationToken)
    {
        var tenantId = GetGuidClaim(user, "tenant_id");
        var integrationId = GetGuidClaim(user, "integration_id");
        if (tenantId is null || integrationId is null)
        {
            return TypedResults.Unauthorized();
        }

        var errors = ValidateReportRequest(request, out var state);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await useCase.ExecuteAsync(
            tenantId.Value,
            integrationId.Value,
            new ReportWhatsAppSessionStateCommand(state, request.Qr, request.PhoneNumber, request.AtUtc!.Value),
            cancellationToken);

        return TypedResults.NoContent();
    }

    private static Dictionary<string, string[]> ValidateReportRequest(
        ReportWhatsAppSessionStateRequest request, out EWhatsAppSessionState state)
    {
        var errors = new Dictionary<string, string[]>();
        void AddError(string field, string message) => errors[field] = [message];

        state = EWhatsAppSessionState.Disconnected;
        if (request.State is null || !ValidStates.Contains(request.State))
        {
            AddError("state", "Deve ser 'qr_pending', 'connected', 'disconnected' ou 'logged_out'.");
        }
        else
        {
            state = ParseState(request.State);
        }

        if (request.AtUtc is null || request.AtUtc.Value.Offset != TimeSpan.Zero)
        {
            AddError("atUtc", "Deve conter um instante UTC válido.");
        }

        if (state == EWhatsAppSessionState.QrPending && string.IsNullOrWhiteSpace(request.Qr))
        {
            AddError("qr", "Obrigatório quando state é 'qr_pending'.");
        }

        if (request.Qr?.Length > 8_192)
        {
            AddError("qr", "Deve ter no máximo 8192 caracteres.");
        }

        if (request.PhoneNumber is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(request.PhoneNumber, "^[1-9][0-9]{7,14}$"))
        {
            AddError("phoneNumber", "Quando informado, deve conter de 8 a 15 dígitos internacionais.");
        }

        return errors;
    }

    private static EWhatsAppSessionState ParseState(string value) => value switch
    {
        "qr_pending" => EWhatsAppSessionState.QrPending,
        "connected" => EWhatsAppSessionState.Connected,
        "disconnected" => EWhatsAppSessionState.Disconnected,
        "logged_out" => EWhatsAppSessionState.LoggedOut,
        _ => throw new InvalidOperationException($"state '{value}' deveria ter sido rejeitado pela validação."),
    };

    // -----------------------------------------------------------------------
    // Get (Office)
    // -----------------------------------------------------------------------

    private static async Task<Results<Ok<WhatsAppSessionStateResponse>, BadRequest<object>, UnauthorizedHttpResult,
        ForbidHttpResult, NotFound<object>>> GetAsync(
        Guid tenantId,
        Guid? integrationId,
        ClaimsPrincipal user,
        IGetWhatsAppSessionStateUseCase useCase,
        CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await useCase.ExecuteAsync(userId.Value, tenantId, integrationId, cancellationToken);
        return result.Outcome switch
        {
            EGetWhatsAppSessionStateOutcome.Forbidden => TypedResults.Forbid(),
            EGetWhatsAppSessionStateOutcome.IntegrationNotFound =>
                TypedResults.NotFound<object>(new { error = "integration_not_found" }),
            EGetWhatsAppSessionStateOutcome.IntegrationAmbiguous =>
                TypedResults.BadRequest<object>(new { error = "integration_id_required" }),
            _ => TypedResults.Ok(ToResponse(result)),
        };
    }

    private static WhatsAppSessionStateResponse ToResponse(GetWhatsAppSessionStateResult result) => new(
        result.State is null ? "unknown" : ToJsonValue(result.State.Value),
        result.Qr,
        result.QrExpiresAtUtc,
        result.PhoneNumber,
        result.UpdatedAtUtc);

    private static string ToJsonValue(EWhatsAppSessionState state) => state switch
    {
        EWhatsAppSessionState.QrPending => "qr_pending",
        EWhatsAppSessionState.Connected => "connected",
        EWhatsAppSessionState.Disconnected => "disconnected",
        EWhatsAppSessionState.LoggedOut => "logged_out",
        _ => throw new InvalidOperationException($"Estado de sessão WhatsApp desconhecido: {state}."),
    };

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}

/// <summary>Evento de estado de sessão reportado pelo processo do Channel.</summary>
/// <param name="State">Um de <c>"qr_pending"</c>, <c>"connected"</c>, <c>"disconnected"</c> ou <c>"logged_out"</c>.</param>
/// <param name="Qr">Texto bruto do QR (não imagem) — obrigatório apenas quando <c>state</c> é <c>"qr_pending"</c>.</param>
/// <param name="PhoneNumber">Telefone pareado — presente apenas quando <c>state</c> é <c>"connected"</c>.</param>
/// <param name="AtUtc">Instante UTC em que o Channel observou esta transição.</param>
public sealed record ReportWhatsAppSessionStateRequest(
    string? State,
    string? Qr,
    string? PhoneNumber,
    DateTimeOffset? AtUtc);

/// <summary>
/// Último estado de pareamento WhatsApp conhecido pela API, devolvido ao
/// Office (ADR-049). <c>state</c> é <c>"unknown"</c> quando nada foi
/// reportado ainda para a integração.
/// </summary>
/// <param name="State"><c>"unknown"</c>, <c>"qr_pending"</c>, <c>"connected"</c>, <c>"disconnected"</c> ou <c>"logged_out"</c>.</param>
/// <param name="Qr">Texto bruto do QR — presente apenas quando <paramref name="State"/> é <c>"qr_pending"</c> e o QR ainda não expirou.</param>
/// <param name="QrExpiresAtUtc">Instante de expiração do QR, quando o último estado reportado foi <c>"qr_pending"</c>.</param>
/// <param name="PhoneNumber">Telefone pareado, quando <paramref name="State"/> é <c>"connected"</c>.</param>
/// <param name="UpdatedAtUtc">Instante do último reporte recebido do Channel.</param>
public sealed record WhatsAppSessionStateResponse(
    string State,
    string? Qr,
    DateTimeOffset? QrExpiresAtUtc,
    string? PhoneNumber,
    DateTimeOffset? UpdatedAtUtc);
