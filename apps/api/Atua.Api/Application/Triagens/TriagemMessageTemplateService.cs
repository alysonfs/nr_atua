using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Triagens;

public sealed class TriagemMessageTemplateService(AtuaDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<TriagemTemplateOperationResult> ListAsync(Guid userId, Guid tenantId,
        Guid integrationId, CancellationToken cancellationToken)
    {
        var access = await ValidateReadAccessAsync(userId, tenantId, integrationId, cancellationToken);
        if (access != ETriagemTemplateOperationStatus.Success)
            return TriagemTemplateOperationResult.Failure(access);

        var templates = await dbContext.TriagemMessageTemplates.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.IntegrationId == integrationId)
            .OrderBy(item => item.Type)
            .ThenByDescending(item => item.Version)
            .ToListAsync(cancellationToken);

        return TriagemTemplateOperationResult.Success(templates.Select(ToView).ToArray());
    }

    public async Task<TriagemTemplateOperationResult> SaveDraftAsync(Guid userId, Guid tenantId,
        Guid integrationId, ETriagemMessageTemplateType type, string content,
        CancellationToken cancellationToken)
    {
        var access = await ValidateAccessAsync(userId, tenantId, integrationId, cancellationToken);
        if (access != ETriagemTemplateOperationStatus.Success)
            return TriagemTemplateOperationResult.Failure(access);

        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 8_000)
            return TriagemTemplateOperationResult.Failure(ETriagemTemplateOperationStatus.InvalidContent);

        var now = timeProvider.GetUtcNow();
        var draft = await dbContext.TriagemMessageTemplates.SingleOrDefaultAsync(item =>
                item.TenantId == tenantId
                && item.IntegrationId == integrationId
                && item.Type == type
                && item.Status == ETriagemMessageTemplateStatus.Draft,
            cancellationToken);

        if (draft is null)
        {
            var nextVersion = (await dbContext.TriagemMessageTemplates
                .Where(item => item.TenantId == tenantId
                    && item.IntegrationId == integrationId
                    && item.Type == type)
                .MaxAsync(item => (int?)item.Version, cancellationToken) ?? 0) + 1;
            draft = TriagemMessageTemplate.CreateDraft(Guid.CreateVersion7(), tenantId, integrationId,
                type, nextVersion, content, now);
            dbContext.TriagemMessageTemplates.Add(draft);
        }
        else
        {
            draft.UpdateDraft(content, now);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TriagemTemplateOperationResult.Failure(
                ETriagemTemplateOperationStatus.ConcurrencyConflict);
        }
        catch (DbUpdateException)
        {
            return TriagemTemplateOperationResult.Failure(
                ETriagemTemplateOperationStatus.ConcurrencyConflict);
        }

        return TriagemTemplateOperationResult.Success([ToView(draft)]);
    }

    public async Task<TriagemTemplateOperationResult> PublishAsync(Guid userId, Guid tenantId,
        Guid integrationId, ETriagemMessageTemplateType type, CancellationToken cancellationToken)
    {
        var access = await ValidateAccessAsync(userId, tenantId, integrationId, cancellationToken);
        if (access != ETriagemTemplateOperationStatus.Success)
            return TriagemTemplateOperationResult.Failure(access);

        var draft = await dbContext.TriagemMessageTemplates.SingleOrDefaultAsync(item =>
                item.TenantId == tenantId
                && item.IntegrationId == integrationId
                && item.Type == type
                && item.Status == ETriagemMessageTemplateStatus.Draft,
            cancellationToken);
        if (draft is null)
            return TriagemTemplateOperationResult.Failure(ETriagemTemplateOperationStatus.DraftNotFound);

        var validationErrors = TriagemMessageTemplateRenderer.Validate(type, draft.Content);
        if (validationErrors.Count > 0)
            return TriagemTemplateOperationResult.ValidationFailure(validationErrors);

        var now = timeProvider.GetUtcNow();
        var active = await dbContext.TriagemMessageTemplates.SingleOrDefaultAsync(item =>
                item.TenantId == tenantId
                && item.IntegrationId == integrationId
                && item.Type == type
                && item.Status == ETriagemMessageTemplateStatus.Published
                && item.IsActive,
            cancellationToken);
        if (dbContext.Database.IsRelational())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (active is not null)
                {
                    active.Deactivate(now);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                draft.Publish(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                return TriagemTemplateOperationResult.Failure(
                    ETriagemTemplateOperationStatus.ConcurrencyConflict);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                return TriagemTemplateOperationResult.Failure(
                    ETriagemTemplateOperationStatus.ConcurrencyConflict);
            }
        }
        else
        {
            active?.Deactivate(now);
            draft.Publish(now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TriagemTemplateOperationResult.Success([ToView(draft)]);
    }

    public async Task<TriagemTemplateOperationResult> PreviewAsync(Guid userId, Guid tenantId,
        Guid integrationId, ETriagemMessageTemplateType type, string content,
        CancellationToken cancellationToken)
    {
        var access = await ValidateAccessAsync(userId, tenantId, integrationId, cancellationToken);
        if (access != ETriagemTemplateOperationStatus.Success)
            return TriagemTemplateOperationResult.Failure(access);

        var errors = TriagemMessageTemplateRenderer.Validate(type, content);
        var preview = string.IsNullOrWhiteSpace(content)
            ? null
            : TriagemMessageTemplateRenderer.RenderPreview(type, content);
        return new TriagemTemplateOperationResult(
            errors.Count == 0
                ? ETriagemTemplateOperationStatus.Success
                : ETriagemTemplateOperationStatus.ValidationFailed,
            [],
            errors,
            preview);
    }

    private async Task<ETriagemTemplateOperationStatus> ValidateAccessAsync(Guid userId, Guid tenantId,
        Guid integrationId, CancellationToken cancellationToken)
    {
        var authorized = await dbContext.TenantMemberships.AsNoTracking().AnyAsync(membership =>
                membership.UserId == userId
                && membership.TenantId == tenantId
                && (membership.Role == ETenantMembershipRole.Owner
                    || membership.Role == ETenantMembershipRole.Admin),
            cancellationToken);
        if (!authorized) return ETriagemTemplateOperationStatus.Forbidden;

        var integrationExists = await dbContext.Integrations.AsNoTracking().AnyAsync(
            item => item.Id == integrationId && item.TenantId == tenantId, cancellationToken);
        return integrationExists
            ? ETriagemTemplateOperationStatus.Success
            : ETriagemTemplateOperationStatus.IntegrationNotFound;
    }

    private async Task<ETriagemTemplateOperationStatus> ValidateReadAccessAsync(Guid userId, Guid tenantId,
        Guid integrationId, CancellationToken cancellationToken)
    {
        var authorized = await dbContext.TenantMemberships.AsNoTracking().AnyAsync(membership =>
            membership.UserId == userId && membership.TenantId == tenantId, cancellationToken);
        if (!authorized) return ETriagemTemplateOperationStatus.Forbidden;

        var integrationExists = await dbContext.Integrations.AsNoTracking().AnyAsync(
            item => item.Id == integrationId && item.TenantId == tenantId, cancellationToken);
        return integrationExists
            ? ETriagemTemplateOperationStatus.Success
            : ETriagemTemplateOperationStatus.IntegrationNotFound;
    }

    private static TriagemMessageTemplateView ToView(TriagemMessageTemplate template) =>
        new(template.Id, template.Type, template.Version, template.Content, template.Status,
            template.IsActive, template.CreatedAtUtc, template.UpdatedAtUtc, template.PublishedAtUtc,
            TriagemMessageTemplateRenderer.Validate(template.Type, template.Content));
}

public sealed record TriagemMessageTemplateView(
    Guid Id,
    ETriagemMessageTemplateType Type,
    int Version,
    string Content,
    ETriagemMessageTemplateStatus Status,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<string> ValidationErrors);

public sealed record TriagemTemplateOperationResult(
    ETriagemTemplateOperationStatus Status,
    IReadOnlyList<TriagemMessageTemplateView> Templates,
    IReadOnlyList<string> ValidationErrors,
    string? Preview)
{
    public static TriagemTemplateOperationResult Success(IReadOnlyList<TriagemMessageTemplateView> templates) =>
        new(ETriagemTemplateOperationStatus.Success, templates, [], null);

    public static TriagemTemplateOperationResult Failure(ETriagemTemplateOperationStatus status) =>
        new(status, [], [], null);

    public static TriagemTemplateOperationResult ValidationFailure(IReadOnlyList<string> errors) =>
        new(ETriagemTemplateOperationStatus.ValidationFailed, [], errors, null);
}
