using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Consulta o último estado de pareamento WhatsApp para o Office (ADR-049).
/// Autorização por papel (OWNER/ADMIN) replica exatamente
/// <c>TriagemMessageTemplateService.ValidateAccessAsync</c>. A integração
/// WhatsApp do tenant é identificada pelas credenciais de serviço com o
/// escopo <see cref="ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope"/>
/// — o mesmo escopo que autoriza o processo do Channel a reivindicar
/// comandos dessa integração (decisão registrada no ADR-049, já que o
/// domínio de integrações não distingue "WhatsApp" de outros provedores).
/// </summary>
public sealed class GetWhatsAppSessionStateUseCase(
    AtuaDbContext dbContext,
    IWhatsAppSessionStateStore store,
    TimeProvider timeProvider) : IGetWhatsAppSessionStateUseCase
{
    public async Task<GetWhatsAppSessionStateResult> ExecuteAsync(Guid userId, Guid tenantId, Guid? integrationId,
        CancellationToken cancellationToken)
    {
        var authorized = await dbContext.TenantMemberships.AsNoTracking().AnyAsync(membership =>
                membership.UserId == userId
                && membership.TenantId == tenantId
                && (membership.Role == ETenantMembershipRole.Owner
                    || membership.Role == ETenantMembershipRole.Admin),
            cancellationToken);
        if (!authorized)
        {
            return GetWhatsAppSessionStateResult.Failure(EGetWhatsAppSessionStateOutcome.Forbidden);
        }

        Guid? resolvedIntegrationId;
        if (integrationId is { } explicitIntegrationId)
        {
            var belongsToTenant = await dbContext.ServiceCredentials.AsNoTracking().AnyAsync(credential =>
                    credential.TenantId == tenantId
                    && credential.IntegrationId == explicitIntegrationId
                    && credential.Scope == ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope
                    && credential.RevokedAt == null,
                cancellationToken);
            if (!belongsToTenant)
            {
                return GetWhatsAppSessionStateResult.Failure(EGetWhatsAppSessionStateOutcome.IntegrationNotFound);
            }

            resolvedIntegrationId = explicitIntegrationId;
        }
        else
        {
            var integrationIds = await dbContext.ServiceCredentials.AsNoTracking()
                .Where(credential => credential.TenantId == tenantId
                    && credential.Scope == ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope
                    && credential.RevokedAt == null)
                .Select(credential => credential.IntegrationId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (integrationIds.Count > 1)
            {
                return GetWhatsAppSessionStateResult.Failure(EGetWhatsAppSessionStateOutcome.IntegrationAmbiguous);
            }

            // Zero integrações resolvidas (Channel ainda não provisionado para
            // este tenant) é equivalente a "nenhum estado conhecido" —
            // devolvido como sucesso com estado "unknown", não como erro.
            resolvedIntegrationId = integrationIds.Count == 1 ? integrationIds[0] : null;
        }

        if (resolvedIntegrationId is null)
        {
            return new GetWhatsAppSessionStateResult(EGetWhatsAppSessionStateOutcome.Success);
        }

        var snapshot = store.Get(tenantId, resolvedIntegrationId.Value, timeProvider.GetUtcNow());
        return snapshot is null
            ? new GetWhatsAppSessionStateResult(EGetWhatsAppSessionStateOutcome.Success)
            : new GetWhatsAppSessionStateResult(
                EGetWhatsAppSessionStateOutcome.Success,
                snapshot.State,
                snapshot.Qr,
                snapshot.QrExpiresAtUtc,
                snapshot.PhoneNumber,
                snapshot.UpdatedAtUtc);
    }
}
