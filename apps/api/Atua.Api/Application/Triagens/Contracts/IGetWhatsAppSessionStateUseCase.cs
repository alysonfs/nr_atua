using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Consulta o estado de pareamento WhatsApp para o Office
/// (<c>GET /api/tenants/{tenantId}/whatsapp-session</c>, ADR-049).
/// Restrito a membros OWNER/ADMIN do tenant — o mesmo padrão de
/// autorização de <c>TriagemMessageTemplateService.ValidateAccessAsync</c>.
/// </summary>
public interface IGetWhatsAppSessionStateUseCase
{
    /// <param name="integrationId">
    /// Opcional. Quando omitido, resolve a única integração WhatsApp ativa
    /// do tenant (identificada pelas credenciais de serviço com o escopo
    /// <c>channel.command.claim</c>); se houver mais de uma, retorna
    /// <see cref="EGetWhatsAppSessionStateOutcome.IntegrationAmbiguous"/> e
    /// exige que o chamador informe explicitamente qual.
    /// </param>
    Task<GetWhatsAppSessionStateResult> ExecuteAsync(Guid userId, Guid tenantId, Guid? integrationId,
        CancellationToken cancellationToken);
}

public enum EGetWhatsAppSessionStateOutcome
{
    Success,

    /// <summary>Usuário autenticado não é OWNER/ADMIN deste tenant (ou não é membro).</summary>
    Forbidden,

    /// <summary><paramref name="integrationId"/> explícito não corresponde a uma integração WhatsApp deste tenant.</summary>
    IntegrationNotFound,

    /// <summary>Mais de uma integração WhatsApp ativa no tenant e nenhum <c>integrationId</c> foi informado.</summary>
    IntegrationAmbiguous,
}

/// <summary>
/// Resultado da consulta. Quando <see cref="Outcome"/> é
/// <see cref="EGetWhatsAppSessionStateOutcome.Success"/> e <see cref="State"/>
/// é <c>null</c>, nenhum estado foi reportado ainda (estado "unknown").
/// </summary>
public sealed record GetWhatsAppSessionStateResult(
    EGetWhatsAppSessionStateOutcome Outcome,
    EWhatsAppSessionState? State = null,
    string? Qr = null,
    DateTimeOffset? QrExpiresAtUtc = null,
    string? PhoneNumber = null,
    DateTimeOffset? UpdatedAtUtc = null)
{
    public static GetWhatsAppSessionStateResult Failure(EGetWhatsAppSessionStateOutcome outcome) => new(outcome);
}
