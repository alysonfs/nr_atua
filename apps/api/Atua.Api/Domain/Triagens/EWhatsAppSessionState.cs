namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Estado de pareamento da sessão Baileys do Channel WhatsApp (ADR-049).
/// Reportado pelo processo do canal e consultado pelo Office para exibir o
/// QR na tela de Configurações (OWNER/ADMIN), já que a EC2 e seu EBS são
/// destruídos a cada <c>make down</c>, perdendo a sessão e exigindo novo
/// pareamento.
/// </summary>
public enum EWhatsAppSessionState
{
    /// <summary>Aguardando leitura do QR — <c>Qr</c> carrega o texto bruto do código.</summary>
    QrPending,

    /// <summary>Sessão pareada e conectada — <c>PhoneNumber</c> carrega o número pareado.</summary>
    Connected,

    /// <summary>Desconexão transitória — o Channel tentará reconectar sem novo pareamento.</summary>
    Disconnected,

    /// <summary>Sessão deslogada pelo WhatsApp — exige novo pareamento por QR.</summary>
    LoggedOut,
}
