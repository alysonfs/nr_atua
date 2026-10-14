namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Estados de uma linha de <see cref="TriagemTriggerOutbox"/> (ADR-041).
///
/// <c>Pending</c> é o estado inicial (gravado pelo Collector) e também o
/// estado de retorno para uma nova tentativa após falha transitória —
/// <see cref="NextAttemptAtUtc"/> em <see cref="TriagemTriggerOutbox"/>
/// controla quando a linha volta a ser elegível para claim.
/// </summary>
public enum ETriagemTriggerOutboxStatus
{
    /// <summary>Aguardando (ou re-aguardando, após retry) claim pela Master API.</summary>
    Pending,

    /// <summary>Reivindicada por uma instância da Master API — sob lease (<see cref="TriagemTriggerOutbox.ClaimExpiresAtUtc"/>).</summary>
    Processing,

    /// <summary>Processada com sucesso (triagem criada ou já existente — ADR-039).</summary>
    Processed,

    /// <summary>
    /// Ignorada definitivamente, sem nova tentativa: a Master API revalidou o
    /// estado atual da OS e ela não existe ou não está mais elegível.
    /// </summary>
    Skipped,

    /// <summary>Esgotou o número máximo de tentativas (<c>TriagemOutbox:MaxAttempts</c>).</summary>
    Failed,
}
