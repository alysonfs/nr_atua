namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Código seguro de erro de <see cref="TriagemTriggerOutbox"/> (ADR-041).
///
/// Deliberadamente não é a mensagem da exceção (<c>Exception.Message</c>):
/// mensagens de exceção podem conter detalhes de infraestrutura (strings de
/// conexão, caminhos, nomes de colunas) que não devem ser persistidos nem
/// logados. Apenas um código controlado e fechado é armazenado em
/// <see cref="TriagemTriggerOutbox.LastErrorCode"/>.
/// </summary>
public enum ETriagemOutboxErrorCode
{
    /// <summary>
    /// A Master API revalidou e a OS referenciada não foi encontrada (tenant +
    /// integração + <c>WorkOrderProviderId</c>). Sem retry — <see cref="ETriagemTriggerOutboxStatus.Skipped"/>.
    /// </summary>
    WorkOrderNotFound,

    /// <summary>
    /// A Master API revalidou e a OS não está mais em status elegível. Sem
    /// retry — <see cref="ETriagemTriggerOutboxStatus.Skipped"/>.
    /// </summary>
    WorkOrderNotEligible,

    /// <summary>
    /// Falha classificada como transitória (ex.: indisponibilidade momentânea
    /// de um recurso externo). Elegível a retry com backoff.
    /// </summary>
    TransientError,

    /// <summary>
    /// Falha não classificada em nenhuma categoria específica acima. Tratada
    /// da mesma forma que <see cref="TransientError"/> (elegível a retry com
    /// backoff) — nunca mascarada como sucesso.
    /// </summary>
    Unclassified,
}
