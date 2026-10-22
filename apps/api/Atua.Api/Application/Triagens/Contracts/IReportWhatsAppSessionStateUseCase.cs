using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Recebe o estado de pareamento reportado pelo processo do Channel
/// (<c>POST /api/internal/channel/session</c>, ADR-049) e o grava no
/// <see cref="IWhatsAppSessionStateStore"/>, sobrescrevendo o último estado
/// conhecido para a tenant+integração da credencial de serviço autenticada.
/// </summary>
public interface IReportWhatsAppSessionStateUseCase
{
    Task ExecuteAsync(Guid tenantId, Guid integrationId, ReportWhatsAppSessionStateCommand command,
        CancellationToken cancellationToken);
}

/// <summary>Evento de estado de sessão reportado pelo Channel.</summary>
/// <param name="State">Estado atual da sessão Baileys.</param>
/// <param name="Qr">Texto bruto do QR — obrigatório quando <paramref name="State"/> é <see cref="EWhatsAppSessionState.QrPending"/>, ignorado nos demais estados.</param>
/// <param name="PhoneNumber">Telefone pareado — considerado apenas quando <paramref name="State"/> é <see cref="EWhatsAppSessionState.Connected"/>.</param>
/// <param name="AtUtc">Instante (UTC) em que o Channel observou esta transição.</param>
public sealed record ReportWhatsAppSessionStateCommand(
    EWhatsAppSessionState State,
    string? Qr,
    string? PhoneNumber,
    DateTimeOffset AtUtc);
