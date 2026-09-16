namespace Atua.Collector.Contracts;

/// <summary>
/// Motivo de falha de um comando de coleta (espelho do enum no lado da API).
/// ATENÇÃO: use <see cref="CredentialRejected"/> SOMENTE quando o login CAS falhar
/// por credencial inválida. Qualquer outra falha deve usar <see cref="IServiceUnavailable"/>
/// ou <see cref="UnexpectedError"/> — o uso incorreto de CredentialRejected desativa o Agente.
/// </summary>
public enum ECommandFailureReason
{
    CredentialRejected,
    IServiceUnavailable,
    ClaimTimeout,
    UnexpectedError,
}
