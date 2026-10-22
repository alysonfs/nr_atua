using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Guarda apenas o ÚLTIMO estado de pareamento WhatsApp (QR/conectado/
/// desconectado/deslogado) por tenant+integração (ADR-049). A implementação
/// padrão é em memória — API e Channel rodam na mesma EC2, destruída junto
/// a cada <c>make down</c>, então não há necessidade de persistência
/// durável nem de uma migration nova.
/// </summary>
public interface IWhatsAppSessionStateStore
{
    void Report(Guid tenantId, Guid integrationId, WhatsAppSessionStateSnapshot snapshot);

    /// <summary>
    /// Retorna o último estado conhecido, ou <c>null</c> quando nada foi
    /// reportado ainda para esta tenant+integração. O QR expirado
    /// (<c>now &gt; QrExpiresAtUtc</c>) nunca é retornado — apenas o campo
    /// <see cref="WhatsAppSessionStateSnapshot.Qr"/> é ocultado; o restante
    /// do snapshot (estado, telefone, instantes) é preservado.
    /// </summary>
    WhatsAppSessionStateSnapshot? Get(Guid tenantId, Guid integrationId, DateTimeOffset now);
}

/// <summary>Último estado de pareamento conhecido para uma tenant+integração.</summary>
/// <param name="State">Estado reportado pelo Channel.</param>
/// <param name="Qr">Texto bruto do QR (não imagem) — presente apenas quando <see cref="State"/> é <see cref="EWhatsAppSessionState.QrPending"/> e ainda não expirou.</param>
/// <param name="QrExpiresAtUtc">Instante de expiração do QR (~60s após o reporte) — preenchido apenas quando o último estado reportado foi <see cref="EWhatsAppSessionState.QrPending"/>.</param>
/// <param name="PhoneNumber">Telefone pareado — presente apenas quando <see cref="State"/> é <see cref="EWhatsAppSessionState.Connected"/>.</param>
/// <param name="UpdatedAtUtc">Instante do último reporte recebido do Channel.</param>
public sealed record WhatsAppSessionStateSnapshot(
    EWhatsAppSessionState State,
    string? Qr,
    DateTimeOffset? QrExpiresAtUtc,
    string? PhoneNumber,
    DateTimeOffset UpdatedAtUtc);
