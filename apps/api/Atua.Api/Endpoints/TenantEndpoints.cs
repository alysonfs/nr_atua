using System.Security.Claims;
using Atua.Api.Application.Billing;
using Atua.Api.Application.Integrations;
using Atua.Api.Application.Tenants;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Endpoints;

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/me/tenants", GetMyTenants)
            .RequireAuthorization("BrowserSession");

        endpoints.MapPost("/api/tenants", CreateTenant)
            .RequireAuthorization("BrowserSession");

        endpoints.MapPut("/api/tenants/{tenantId:guid}/integrations/{integrationId:guid}/credentials",
                SetCredentials)
            .RequireAuthorization("BrowserSession");

        endpoints.MapGet("/api/tenants/{tenantId:guid}/integrations/{integrationId:guid}/credentials",
                GetCredentials)
            .RequireAuthorization("BrowserSession");

        endpoints.MapPost(
                "/api/tenants/{tenantId:guid}/integrations/{integrationId:guid}/credentials/validate",
                ValidateCredentials)
            .RequireAuthorization("BrowserSession");

        endpoints.MapGet("/api/tenants/{tenantId:guid}/plan", GetTenantPlan)
            .RequireAuthorization("BrowserSession");

        endpoints.MapPut("/api/tenants/{tenantId:guid}/plan", ChangeTenantPlan)
            .RequireAuthorization("BrowserSession");

        endpoints.MapPut("/api/tenants/{tenantId:guid}/memberships/{targetUserId:guid}/role",
                ChangeTenantMembershipRole)
            .RequireAuthorization("BrowserSession");
    }

    private static async Task<IResult> GetMyTenants(ClaimsPrincipal user, AtuaDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var memberships = await db.TenantMemberships.AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Join(db.Tenants.AsNoTracking(), membership => membership.TenantId, tenant => tenant.Id,
                (membership, tenant) => new { tenant.Id, tenant.Name, membership.Role })
            .Join(db.Integrations.AsNoTracking(), left => left.Id, integration => integration.TenantId,
                (left, integration) => new
                {
                    left.Id, left.Name, left.Role, IntegrationId = integration.Id
                })
            .ToListAsync(cancellationToken);

        var tenants = memberships.Select(item =>
            new TenantMembershipResponse(item.Id, item.Name, item.Role.ToString().ToUpperInvariant(),
                item.IntegrationId))
            .ToArray();

        Guid? defaultTenantId = tenants.Length == 1 ? tenants[0].TenantId : null;

        return Results.Ok(new GetMyTenantsResponse(tenants, defaultTenantId));
    }

    private static async Task<IResult> CreateTenant(ClaimsPrincipal user, CreateTenantRequest request,
        AddTenantUseCase service, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var result = await service.ExecuteAsync(userId.Value, request.Name, request.Cnpj,
            cancellationToken);

        return result.Status switch
        {
            ECreateTenantStatus.Success => Results.Created($"/api/tenants/{result.TenantId}",
                new CreateTenantResponse(result.TenantId!.Value, result.IntegrationId!.Value)),
            ECreateTenantStatus.InvalidCnpj => Results.BadRequest(new { error = "invalid_cnpj" }),
            ECreateTenantStatus.CnpjAlreadyRegistered => Results.Conflict(
                new { error = "cnpj_already_registered" }),
            ECreateTenantStatus.UserAlreadyHasTenant => Results.Conflict(
                new { error = "user_already_has_tenant" }),
            ECreateTenantStatus.EmailNotConfirmed => Results.Conflict(
                new { error = "email_not_confirmed" }),
            ECreateTenantStatus.PlanCatalogInconsistent => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> SetCredentials(Guid tenantId, Guid integrationId,
        ClaimsPrincipal user, SetCredentialsRequest request, IServiceCredentialService service,
        CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var status = await service.SetCredentialsAsync(userId.Value, tenantId, integrationId,
            request.Username, request.Password, request.BaseUrl, cancellationToken);

        return status switch
        {
            ESetCredentialsStatus.Success => Results.NoContent(),
            ESetCredentialsStatus.Forbidden => Results.Forbid(),
            ESetCredentialsStatus.IntegrationNotFound => Results.NotFound(
                new { error = "integration_not_found" }),
            ESetCredentialsStatus.InvalidCredentials => Results.BadRequest(
                new { error = "invalid_credentials" }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> GetCredentials(Guid tenantId, Guid integrationId,
        ClaimsPrincipal user, IServiceCredentialService service, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var result = await service.GetStatusAsync(userId.Value, tenantId, integrationId, cancellationToken);
        if (result is null) return Results.Forbid();

        return Results.Ok(new GetCredentialsResponse(result.HasCredentials,
            result.ValidationStatus.ToString(), result.LastValidatedAtUtc, result.UpdatedAtUtc));
    }

    private static async Task<IResult> ValidateCredentials(Guid tenantId, Guid integrationId,
        ClaimsPrincipal user, IServiceCredentialValidationService service,
        CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var result = await service.ValidateAsync(userId.Value, tenantId, integrationId, cancellationToken);
        if (result is null) return Results.Forbid();
        if (!result.CredentialsConfigured)
        {
            return Results.NotFound(new { error = "credentials_not_configured" });
        }

        return Results.Ok(new ValidateCredentialsResponse(result.ValidationStatus!.Value.ToString(),
            result.EvaluatedAtUtc!.Value));
    }

    private static async Task<IResult> GetTenantPlan(Guid tenantId, ClaimsPrincipal user,
        GetTenantPlanUseCase service, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var result = await service.ExecuteAsync(userId.Value, tenantId, cancellationToken);
        if (result is null) return Results.Forbid();
        if (!result.IsConsistent)
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(new GetTenantPlanResponse(result.PlanName!, result.IsFree!.Value,
            result.DaysRemaining, result.MaxIntegrations!.Value, result.UsedIntegrations!.Value,
            result.MaxUsers!.Value, result.UsedUsers!.Value, result.Status!));
    }

    private static async Task<IResult> ChangeTenantPlan(Guid tenantId, ClaimsPrincipal user,
        ChangeTenantPlanRequest request, ChangeTenantPlanUseCase service,
        CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var status = await service.ExecuteAsync(userId.Value, tenantId, request.PlanCode,
            cancellationToken);

        return status switch
        {
            EChangeTenantPlanStatus.Success => Results.NoContent(),
            EChangeTenantPlanStatus.Forbidden => Results.Forbid(),
            EChangeTenantPlanStatus.PlanNotFound => Results.NotFound(new { error = "plan_not_found" }),
            EChangeTenantPlanStatus.NoActivePlan => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> ChangeTenantMembershipRole(Guid tenantId, Guid targetUserId,
        ClaimsPrincipal user, ChangeTenantMembershipRoleRequest request,
        ChangeTenantMembershipRoleUseCase service, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        if (!Enum.TryParse<ETenantMembershipRole>(request.Role, ignoreCase: true, out var newRole))
        {
            return Results.BadRequest(new { error = "invalid_role" });
        }

        var status = await service.ExecuteAsync(userId.Value, tenantId, targetUserId, newRole,
            cancellationToken);

        return status switch
        {
            EChangeMembershipRoleStatus.Success => Results.NoContent(),
            EChangeMembershipRoleStatus.Forbidden => Results.Forbid(),
            EChangeMembershipRoleStatus.MembershipNotFound => Results.NotFound(
                new { error = "membership_not_found" }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}

public sealed record TenantMembershipResponse(Guid TenantId, string Name, string Role, Guid IntegrationId);
public sealed record GetMyTenantsResponse(IReadOnlyCollection<TenantMembershipResponse> Tenants,
    Guid? DefaultTenantId);

public sealed record CreateTenantRequest(string Name, string Cnpj);
public sealed record CreateTenantResponse(Guid TenantId, Guid IntegrationId);

public sealed record SetCredentialsRequest(string Username, string Password, string? BaseUrl);
public sealed record GetCredentialsResponse(bool HasCredentials, string ValidationStatus,
    DateTimeOffset? LastValidatedAtUtc, DateTimeOffset? UpdatedAtUtc);

public sealed record ValidateCredentialsResponse(string ValidationStatus, DateTimeOffset EvaluatedAtUtc);

public sealed record GetTenantPlanResponse(string PlanName, bool IsFree, int? DaysRemaining,
    int MaxIntegrations, int UsedIntegrations, int MaxUsers, int UsedUsers, string Status);

public sealed record ChangeTenantPlanRequest(string PlanCode);

public sealed record ChangeTenantMembershipRoleRequest(string Role);
