using System.Security.Claims;
using Atua.Api.Application.Triagens;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Endpoints;

public static class TriagemMessageTemplateEndpoints
{
    private const string BaseRoute =
        "/api/tenants/{tenantId:guid}/integrations/{integrationId:guid}/triage-message-templates";

    public static void MapTriagemMessageTemplateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(BaseRoute, ListAsync)
            .RequireAuthorization("BrowserSession");
        endpoints.MapPut($"{BaseRoute}/{{type}}/draft", SaveDraftAsync)
            .RequireAuthorization("BrowserSession");
        endpoints.MapPost($"{BaseRoute}/{{type}}/publish", PublishAsync)
            .RequireAuthorization("BrowserSession");
        endpoints.MapPost($"{BaseRoute}/{{type}}/preview", PreviewAsync)
            .RequireAuthorization("BrowserSession");
    }

    private static async Task<IResult> ListAsync(Guid tenantId, Guid integrationId, ClaimsPrincipal user,
        TriagemMessageTemplateService service, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null) return Results.Unauthorized();
        return ToHttpResult(await service.ListAsync(userId.Value, tenantId, integrationId, cancellationToken));
    }

    private static async Task<IResult> SaveDraftAsync(Guid tenantId, Guid integrationId, string type,
        SaveTriagemMessageTemplateDraftRequest request, ClaimsPrincipal user,
        TriagemMessageTemplateService service, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null) return Results.Unauthorized();
        if (!TryParseType(type, out var parsedType))
            return Results.BadRequest(new { error = "invalid_template_type" });

        return ToHttpResult(await service.SaveDraftAsync(userId.Value, tenantId, integrationId,
            parsedType, request.Content, cancellationToken));
    }

    private static async Task<IResult> PublishAsync(Guid tenantId, Guid integrationId, string type,
        ClaimsPrincipal user, TriagemMessageTemplateService service, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null) return Results.Unauthorized();
        if (!TryParseType(type, out var parsedType))
            return Results.BadRequest(new { error = "invalid_template_type" });

        return ToHttpResult(await service.PublishAsync(userId.Value, tenantId, integrationId,
            parsedType, cancellationToken));
    }

    private static async Task<IResult> PreviewAsync(Guid tenantId, Guid integrationId, string type,
        PreviewTriagemMessageTemplateRequest request, ClaimsPrincipal user,
        TriagemMessageTemplateService service, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null) return Results.Unauthorized();
        if (!TryParseType(type, out var parsedType))
            return Results.BadRequest(new { error = "invalid_template_type" });

        return ToHttpResult(await service.PreviewAsync(userId.Value, tenantId, integrationId,
            parsedType, request.Content, cancellationToken));
    }

    private static IResult ToHttpResult(TriagemTemplateOperationResult result) => result.Status switch
    {
        ETriagemTemplateOperationStatus.Success => Results.Ok(ToResponse(result)),
        ETriagemTemplateOperationStatus.Forbidden => Results.Forbid(),
        ETriagemTemplateOperationStatus.IntegrationNotFound =>
            Results.NotFound(new { error = "integration_not_found" }),
        ETriagemTemplateOperationStatus.InvalidContent =>
            Results.BadRequest(new { error = "invalid_template_content" }),
        ETriagemTemplateOperationStatus.DraftNotFound =>
            Results.NotFound(new { error = "template_draft_not_found" }),
        ETriagemTemplateOperationStatus.ValidationFailed =>
            Results.BadRequest(ToResponse(result)),
        ETriagemTemplateOperationStatus.ConcurrencyConflict =>
            Results.Conflict(new { error = "template_concurrency_conflict" }),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    private static bool TryParseType(string value, out ETriagemMessageTemplateType type)
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse(normalized, ignoreCase: true, out type);
    }

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var value) ? value : null;

    private static TriagemMessageTemplatesResponse ToResponse(TriagemTemplateOperationResult result) =>
        new(result.Templates.Select(template => new TriagemMessageTemplateResponse(
                template.Id,
                template.Type.ToString(),
                template.Version,
                template.Content,
                template.Status.ToString(),
                template.IsActive,
                template.CreatedAtUtc,
                template.UpdatedAtUtc,
                template.PublishedAtUtc,
                template.ValidationErrors))
            .ToArray(),
            result.ValidationErrors,
            result.Preview);
}

/// <summary>Payload para salvar o rascunho de um template da triagem.</summary>
public sealed record SaveTriagemMessageTemplateDraftRequest(string Content);

/// <summary>Payload para validar e renderizar o preview de um template.</summary>
public sealed record PreviewTriagemMessageTemplateRequest(string Content);

/// <summary>Resposta administrativa da gestão de templates.</summary>
public sealed record TriagemMessageTemplatesResponse(
    IReadOnlyList<TriagemMessageTemplateResponse> Templates,
    IReadOnlyList<string> ValidationErrors,
    string? Preview);

/// <summary>Versão administrativa serializada com enums textuais estáveis.</summary>
public sealed record TriagemMessageTemplateResponse(
    Guid Id,
    string Type,
    int Version,
    string Content,
    string Status,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<string> ValidationErrors);
