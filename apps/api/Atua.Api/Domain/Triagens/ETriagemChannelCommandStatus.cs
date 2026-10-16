namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Estados do comando de canal (<see cref="TriagemChannelCommand"/>), ADR-042.
/// </summary>
public enum ETriagemChannelCommandStatus
{
    /// <summary>Aguardando reivindicação por uma instância do canal.</summary>
    Pending,

    /// <summary>
    /// Reivindicado por uma instância do canal (<c>claim</c>), com lease
    /// ativa. Volta a <see cref="Pending"/> automaticamente quando a lease
    /// expira, sem alterar a chave de idempotência.
    /// </summary>
    Claimed,

    /// <summary>
    /// Concluído com sucesso — estado terminal. A triagem avançou para
    /// <c>ContatoIniciado</c>.
    /// </summary>
    Completed,

    /// <summary>
    /// Falha definitiva (reportada pelo canal ou após a quinta falha
    /// transitória consecutiva) — estado terminal. A triagem avançou para
    /// <c>FalhaDeContato</c>.
    /// </summary>
    Failed,
}
