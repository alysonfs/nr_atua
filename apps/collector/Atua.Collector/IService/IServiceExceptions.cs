namespace Atua.Collector.IService;

/// <summary>
/// Lançada quando o login CAS falha por credencial inválida.
/// Mapeia para <c>ECommandFailureReason.CredentialRejected</c>.
/// </summary>
public sealed class CredentialRejectedEx(string message) : Exception(message);

/// <summary>
/// Lançada quando o portal iService está inacessível, lento demais ou apresenta
/// erro não relacionado à credencial.
/// Mapeia para <c>ECommandFailureReason.IServiceUnavailable</c>.
/// </summary>
public sealed class IServiceUnavailableEx(string message, Exception? inner = null)
    : Exception(message, inner);
