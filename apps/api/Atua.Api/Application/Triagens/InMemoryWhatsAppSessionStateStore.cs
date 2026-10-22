using System.Collections.Concurrent;
using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação em memória de <see cref="IWhatsAppSessionStateStore"/>
/// (ADR-049). Registrada como singleton — o processo da API e o do Channel
/// compartilham a mesma EC2, recriada do zero a cada <c>make down</c>, logo
/// não há necessidade de persistência durável nem de uma tabela/migration
/// nova para algo que só vive enquanto a instância está de pé.
/// </summary>
public sealed class InMemoryWhatsAppSessionStateStore : IWhatsAppSessionStateStore
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid IntegrationId), WhatsAppSessionStateSnapshot> entries = new();

    public void Report(Guid tenantId, Guid integrationId, WhatsAppSessionStateSnapshot snapshot)
    {
        entries[(tenantId, integrationId)] = snapshot;
    }

    public WhatsAppSessionStateSnapshot? Get(Guid tenantId, Guid integrationId, DateTimeOffset now)
    {
        if (!entries.TryGetValue((tenantId, integrationId), out var snapshot))
        {
            return null;
        }

        if (snapshot.Qr is not null && snapshot.QrExpiresAtUtc is { } qrExpiresAtUtc && now > qrExpiresAtUtc)
        {
            return snapshot with { Qr = null };
        }

        return snapshot;
    }
}
