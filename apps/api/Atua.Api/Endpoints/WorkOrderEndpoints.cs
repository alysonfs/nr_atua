using System.Security.Claims;
using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Endpoints;

/// <summary>
/// RF-017: consultas de leitura sobre OS para o Dashboard (substituição dos
/// hooks mockados do Office — ver
/// <c>docs/architecture/dashboard-work-order-queries.md</c>).
/// </summary>
public static class WorkOrderEndpoints
{
    public static void MapWorkOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId:guid}/work-orders/summary", GetMonthlySummary)
            .RequireAuthorization("BrowserSession");

        endpoints.MapGet("/api/tenants/{tenantId:guid}/work-orders", GetListByStatus)
            .RequireAuthorization("BrowserSession");

        endpoints.MapGet("/api/tenants/{tenantId:guid}/work-orders/status-summary", GetStatusSummary)
            .RequireAuthorization("BrowserSession");
    }

    private static async Task<IResult> GetMonthlySummary(Guid tenantId, string? month, ClaimsPrincipal user,
        AtuaDbContext db, IWorkOrderMonthlySummaryQuery query, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        if (!TryParseMonth(month, out var monthStart))
        {
            return Results.BadRequest(new { error = "invalid_month" });
        }

        var tenant = await GetTenantIfMemberAsync(db, tenantId, userId.Value, cancellationToken);
        if (tenant is null) return Results.Forbid();

        var result = await query.ExecuteAsync(tenantId, monthStart, tenant.TimeZoneId, cancellationToken);

        var response = new WorkOrderMonthlySummaryResponse(result.Month, result.TimeZoneId,
            result.Statuses
                .Select(status => new WorkOrderStatusMonthSummary(status.Status, status.Total,
                    status.DailyCounts))
                .ToArray());

        return Results.Ok(response);
    }

    private static async Task<IResult> GetListByStatus(Guid tenantId, string? status, int? page,
        int? pageSize, ClaimsPrincipal user, AtuaDbContext db, IWorkOrderListByStatusQuery query,
        CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(status))
        {
            return Results.BadRequest(new { error = "invalid_status" });
        }

        var effectivePage = page ?? 1;
        var effectivePageSize = pageSize ?? 20;
        if (effectivePage < 1 || effectivePageSize is < 1 or > 100)
        {
            return Results.BadRequest(new { error = "invalid_pagination" });
        }

        var isMember = await db.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == userId.Value && membership.TenantId == tenantId,
            cancellationToken);
        if (!isMember) return Results.Forbid();

        var result = await query.ExecuteAsync(tenantId, status, effectivePage, effectivePageSize,
            cancellationToken);

        var response = new WorkOrderListResponse(result.Status, result.Page, result.PageSize,
            result.TotalCount,
            result.Items
                .Select(item => new WorkOrderListItem(item.Id, item.ProviderId, item.Status, item.CreatedAt,
                    item.UpdatedAt))
                .ToArray());

        return Results.Ok(response);
    }

    private static async Task<IResult> GetStatusSummary(Guid tenantId, ClaimsPrincipal user, AtuaDbContext db,
        IWorkOrderStatusSummaryQuery query, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var isMember = await db.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == userId.Value && membership.TenantId == tenantId,
            cancellationToken);
        if (!isMember) return Results.Forbid();

        var result = await query.ExecuteAsync(tenantId, cancellationToken);

        var response = new WorkOrderStatusSummaryResponse(
            result.Statuses.Select(status => new WorkOrderStatusCount(status.Status, status.Total)).ToArray());

        return Results.Ok(response);
    }

    private static async Task<Domain.Tenants.Tenant?> GetTenantIfMemberAsync(AtuaDbContext db, Guid tenantId,
        Guid userId, CancellationToken cancellationToken)
    {
        var isMember = await db.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == userId && membership.TenantId == tenantId, cancellationToken);
        if (!isMember) return null;

        return await db.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);
    }

    private static bool TryParseMonth(string? month, out DateOnly monthStart)
    {
        monthStart = default;
        if (string.IsNullOrWhiteSpace(month)) return false;

        return DateOnly.TryParseExact(month, "yyyy-MM", out monthStart);
    }

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}

public sealed record WorkOrderMonthlySummaryResponse(string Month, string TimeZoneId,
    IReadOnlyList<WorkOrderStatusMonthSummary> Statuses);

public sealed record WorkOrderStatusMonthSummary(string Status, int Total,
    IReadOnlyList<int> DailyCounts);

public sealed record WorkOrderListResponse(string Status, int Page, int PageSize, int TotalCount,
    IReadOnlyList<WorkOrderListItem> Items);

public sealed record WorkOrderListItem(Guid Id, string ProviderId, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record WorkOrderStatusSummaryResponse(IReadOnlyList<WorkOrderStatusCount> Statuses);

public sealed record WorkOrderStatusCount(string Status, int Total);
