using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Grava o estado de pareamento reportado pelo Channel no
/// <see cref="IWhatsAppSessionStateStore"/> (ADR-049). Calcula a expiração
/// do QR (~60s a partir do instante reportado) — apenas eventos
/// <see cref="EWhatsAppSessionState.QrPending"/> carregam QR e expiração;
/// os demais estados zeram ambos os campos.
/// </summary>
public sealed class ReportWhatsAppSessionStateUseCase(IWhatsAppSessionStateStore store)
    : IReportWhatsAppSessionStateUseCase
{
    /// <summary>Tempo de vida do QR exibido ao Office (ADR-049) — alinhado ao ciclo de renovação do Baileys.</summary>
    public static readonly TimeSpan QrTimeToLive = TimeSpan.FromSeconds(60);

    public Task ExecuteAsync(Guid tenantId, Guid integrationId, ReportWhatsAppSessionStateCommand command,
        CancellationToken cancellationToken)
    {
        var isQrPending = command.State == EWhatsAppSessionState.QrPending;
        var isConnected = command.State == EWhatsAppSessionState.Connected;

        store.Report(tenantId, integrationId, new WhatsAppSessionStateSnapshot(
            command.State,
            isQrPending ? command.Qr : null,
            isQrPending ? command.AtUtc + QrTimeToLive : null,
            isConnected ? command.PhoneNumber : null,
            command.AtUtc));

        return Task.CompletedTask;
    }
}
