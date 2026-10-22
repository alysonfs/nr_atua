using System.Security.Claims;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Endpoints;

/// <summary>
/// Pool compartilhado de triagem — visão do operador (épico "Pool de
/// triagem — visão do operador", blocos C1/C2/C3). Rotas autenticadas por
/// sessão do Office (<c>BrowserSession</c>), com isolamento por tenant
/// obrigatório.
/// </summary>
public static class TriagemPoolEndpoints
{
    private const string BaseRoute = "/api/tenants/{tenantId:guid}/triage-pool";

    public static void MapTriagemPoolEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(BaseRoute, ListAsync)
            .RequireAuthorization("BrowserSession");

        endpoints.MapGet($"{BaseRoute}/{{triagemId:guid}}", GetDetailAsync)
            .RequireAuthorization("BrowserSession");

        endpoints.MapPost($"{BaseRoute}/{{triagemId:guid}}/assume", AssumeAsync)
            .RequireAuthorization("BrowserSession");
    }

    private static async Task<IResult> ListAsync(Guid tenantId, string? status, ClaimsPrincipal user,
        IListTriagemPoolUseCase useCase, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null) return Results.Unauthorized();

        ETriagemStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ETriagemStatus>(status, ignoreCase: true, out var parsed))
            {
                return Results.BadRequest(new { error = "invalid_status_filter" });
            }

            statusFilter = parsed;
        }

        var result = await useCase.ExecuteAsync(
            new TriagemPoolListQuery(tenantId, userId.Value, statusFilter), cancellationToken);

        return result.Outcome switch
        {
            ETriagemPoolListOutcome.Success => Results.Ok(ToResponse(result)),
            ETriagemPoolListOutcome.Forbidden => Results.Forbid(),
            ETriagemPoolListOutcome.InvalidStatusFilter => Results.BadRequest(new { error = "invalid_status_filter" }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> GetDetailAsync(Guid tenantId, Guid triagemId, ClaimsPrincipal user,
        IGetTriagemPoolDetailUseCase useCase, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null) return Results.Unauthorized();

        var result = await useCase.ExecuteAsync(tenantId, triagemId, userId.Value, cancellationToken);

        return result.Outcome switch
        {
            ETriagemPoolDetailOutcome.Success => Results.Ok(ToResponse(result.Detail!)),
            ETriagemPoolDetailOutcome.Forbidden => Results.Forbid(),
            ETriagemPoolDetailOutcome.NotFound => Results.NotFound(new { error = "triagem_not_found" }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> AssumeAsync(Guid tenantId, Guid triagemId, ClaimsPrincipal user,
        IAssumeTriagemUseCase useCase, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null) return Results.Unauthorized();

        var result = await useCase.ExecuteAsync(tenantId, triagemId, userId.Value, cancellationToken);

        return result.Outcome switch
        {
            EAssumeTriagemOutcome.Success => Results.Ok(new AssumeTriagemResponse(
                triagemId, result.CurrentStatus!.Value.ToString(),
                result.CurrentAssumidaPorUserId!.Value, result.CurrentAssumidaEmUtc!.Value)),

            EAssumeTriagemOutcome.Forbidden => Results.Forbid(),

            EAssumeTriagemOutcome.NotFound => Results.NotFound(new { error = "triagem_not_found" }),

            EAssumeTriagemOutcome.NotInPool or EAssumeTriagemOutcome.ConcurrencyConflict => Results.Conflict(
                new AssumeTriagemConflictResponse(
                    error: "triagem_already_assumed",
                    status: result.CurrentStatus?.ToString(),
                    assumedByUserId: result.CurrentAssumidaPorUserId,
                    assumedAtUtc: result.CurrentAssumidaEmUtc)),

            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var value) ? value : null;

    private static TriagemPoolListResponse ToResponse(TriagemPoolListResult result) =>
        new(result.TotalCount, result.Items.Select(item => new TriagemPoolListItemResponse(
                item.TriagemId,
                item.WorkOrderProviderNo,
                item.CustomerName,
                item.ProductModel,
                item.ProductBrand,
                item.Status.ToString(),
                item.ChecklistItemsCompleted,
                item.ChecklistItemsTotal,
                item.EnteredPoolAtUtc,
                item.HasNewActivity))
            .ToArray());

    private static TriagemPoolDetailResponse ToResponse(TriagemPoolDetailView detail) =>
        new(
            detail.TriagemId,
            detail.Status.ToString(),
            detail.StateReason,
            detail.EnteredPoolAtUtc,
            detail.AssumidaPorUserId,
            detail.AssumidaEmUtc,
            new TriagemPoolWorkOrderResponse(
                detail.WorkOrder.WorkOrderId,
                detail.WorkOrder.WorkOrderProviderNo,
                detail.WorkOrder.CustomerName,
                detail.WorkOrder.CustomerCpf,
                detail.WorkOrder.ContactPhone,
                detail.WorkOrder.ContactEmail,
                detail.WorkOrder.Address,
                detail.WorkOrder.ZipCode,
                detail.WorkOrder.CityName,
                detail.WorkOrder.StateName,
                detail.WorkOrder.ProductBrand,
                detail.WorkOrder.ProductModel,
                detail.WorkOrder.Symptom),
            detail.Checklist.Select(item => new TriagemPoolChecklistItemResponse(
                    item.Item.ToString(), item.Required, item.Completed, item.Value))
                .ToArray(),
            detail.Attachments.Select(attachment => new TriagemPoolAttachmentResponse(
                    attachment.MediaId, attachment.MediaKind.ToString(), attachment.ContentType,
                    attachment.IsVoiceNote, attachment.DurationSeconds, attachment.Caption,
                    attachment.ReceivedAtUtc, attachment.Url.ToString(), attachment.UrlExpiresAtUtc))
                .ToArray(),
            detail.Conversation.Select(entry => new TriagemPoolConversationEntryResponse(
                    entry.AtUtc, entry.Sender, entry.Kind, entry.Text, entry.AttachmentMediaId))
                .ToArray());
}

public sealed record TriagemPoolListResponse(int TotalCount, IReadOnlyList<TriagemPoolListItemResponse> Items);

public sealed record TriagemPoolListItemResponse(
    Guid TriagemId,
    string? WorkOrderProviderNo,
    string? CustomerName,
    string? ProductModel,
    string? ProductBrand,
    string Status,
    int ChecklistItemsCompleted,
    int ChecklistItemsTotal,
    DateTimeOffset EnteredPoolAtUtc,
    bool HasNewActivity);

public sealed record TriagemPoolDetailResponse(
    Guid TriagemId,
    string Status,
    string StateReason,
    DateTimeOffset EnteredPoolAtUtc,
    Guid? AssumidaPorUserId,
    DateTimeOffset? AssumidaEmUtc,
    TriagemPoolWorkOrderResponse WorkOrder,
    IReadOnlyList<TriagemPoolChecklistItemResponse> Checklist,
    IReadOnlyList<TriagemPoolAttachmentResponse> Attachments,
    IReadOnlyList<TriagemPoolConversationEntryResponse> Conversation);

public sealed record TriagemPoolWorkOrderResponse(
    Guid WorkOrderId,
    string? WorkOrderProviderNo,
    string? CustomerName,
    string? CustomerCpf,
    string? ContactPhone,
    string? ContactEmail,
    string? Address,
    string? ZipCode,
    string? CityName,
    string? StateName,
    string? ProductBrand,
    string? ProductModel,
    string? Symptom);

public sealed record TriagemPoolChecklistItemResponse(string Item, bool Required, bool Completed, string? Value);

public sealed record TriagemPoolAttachmentResponse(
    Guid MediaId,
    string MediaKind,
    string ContentType,
    bool IsVoiceNote,
    int? DurationSeconds,
    string? Caption,
    DateTimeOffset ReceivedAtUtc,
    string Url,
    DateTimeOffset UrlExpiresAtUtc);

public sealed record TriagemPoolConversationEntryResponse(
    DateTimeOffset AtUtc,
    string Sender,
    string Kind,
    string? Text,
    Guid? AttachmentMediaId);

public sealed record AssumeTriagemResponse(
    Guid TriagemId,
    string Status,
    Guid AssumidaPorUserId,
    DateTimeOffset AssumidaEmUtc);

public sealed record AssumeTriagemConflictResponse(
    string error,
    string? status,
    Guid? assumedByUserId,
    DateTimeOffset? assumedAtUtc);
