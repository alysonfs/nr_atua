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

        endpoints.MapGet("/api/tenants/{tenantId:guid}/work-orders/{workOrderId:guid}", GetDetail)
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
                .Select(item => new WorkOrderListItem(item.Id, item.WorkOrderProviderId, item.WorkOrderProviderNo,
                    item.ServiceRequestId, item.Amount,
                    item.Status, item.CreatedAt, item.UpdatedAt, item.ProviderCreatedAt, item.ProviderUpdatedAt,
                    item.ProductModel, item.ProductBrand, item.CustomerName, item.CityName, item.ProviderName))
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

    private static async Task<IResult> GetDetail(Guid tenantId, Guid workOrderId, ClaimsPrincipal user,
        AtuaDbContext db, IWorkOrderDetailQuery query, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var isMember = await db.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == userId.Value && membership.TenantId == tenantId,
            cancellationToken);
        if (!isMember) return Results.Forbid();

        var result = await query.ExecuteAsync(tenantId, workOrderId, cancellationToken);
        if (result is null) return Results.NotFound();

        var response = new WorkOrderDetailResponse(
            result.Id, result.WorkOrderProviderId, result.WorkOrderProviderNo, result.Status,
            result.CreatedAt, result.UpdatedAt, result.ProviderCreatedAt, result.ProviderUpdatedAt,
            result.ServiceRequestId, result.Amount,
            result.CustomerType, result.CustomerName, result.CustomerCpf,
            result.ContactEmail, result.ContactPhone, result.ContactName,
            result.Address, result.ZipCode, result.CountryName, result.StateName, result.CityName,
            result.ProductBrand, result.PdCode, result.CategoryId, result.ProductCategoryCode,
            result.ProductCode, result.ProductModel, result.ProductStatus, result.Symptom,
            result.History.Select(entry => new WorkOrderHistoryEntryResponse(entry.Status, entry.CreatedAt))
                .ToArray());

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

public sealed record WorkOrderListItem(Guid Id, string WorkOrderProviderId, string? WorkOrderProviderNo,
    string? ServiceRequestId, decimal? Amount, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ProviderCreatedAt, DateTimeOffset? ProviderUpdatedAt,
    string? ProductModel, string? ProductBrand, string? CustomerName, string? CityName, string ProviderName);

public sealed record WorkOrderStatusSummaryResponse(IReadOnlyList<WorkOrderStatusCount> Statuses);

public sealed record WorkOrderStatusCount(string Status, int Total);

public sealed record WorkOrderDetailResponse(
    Guid Id, string WorkOrderProviderId, string? WorkOrderProviderNo, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ProviderCreatedAt, DateTimeOffset? ProviderUpdatedAt,
    string? ServiceRequestId, decimal? Amount,
    string? CustomerType, string? CustomerName, string? CustomerCpf,
    string? ContactEmail, string? ContactPhone, string? ContactName,
    string? Address, string? ZipCode, string? CountryName, string? StateName, string? CityName,
    string? ProductBrand, string? PdCode, string? CategoryId, string? ProductCategoryCode,
    string? ProductCode, string? ProductModel, string? ProductStatus, string? Symptom,
    IReadOnlyList<WorkOrderHistoryEntryResponse> History);

public sealed record WorkOrderHistoryEntryResponse(string Status, DateTimeOffset CreatedAt);
