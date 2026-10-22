namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Resultado técnico do processamento de uma mídia recebida pelo canal
/// WhatsApp. Mídia é um evento único — nunca também contabilizada como
/// <see cref="ETriagemInboundMessageOutcome"/> (<c>MessageReceived</c>).
/// </summary>
public enum ETriagemInboundMediaOutcome
{
    /// <summary>Mensagem de grupo — nunca relatada nem associada a uma triagem.</summary>
    IgnoradaGrupo,

    /// <summary>Mídia enviada pelo próprio número conectado do posto.</summary>
    IgnoradaPropria,

    /// <summary><c>mediaKind</c>/<c>contentType</c> não aceitos ou assinatura do arquivo não corresponde ao tipo declarado.</summary>
    RejeitadaTipo,

    /// <summary>Tamanho declarado ou real do arquivo excede o limite de 16 MiB.</summary>
    RejeitadaTamanho,

    /// <summary>Nenhuma triagem elegível para o remetente — binário não é armazenado.</summary>
    SemTriagem,

    /// <summary>Mais de uma triagem candidata simultaneamente — binário não é armazenado.</summary>
    Ambigua,

    /// <summary>Triagem única em <c>ContatoIniciado</c> — mídia armazenada e triagem move para <c>EmColeta</c>.</summary>
    ColetaIniciada,

    /// <summary>
    /// Triagem única encontrada em qualquer status — mídia armazenada, sem
    /// transição de estado (já em <c>EmColeta</c> ou em outro status
    /// qualquer; só <c>ContatoIniciado</c> produz <see cref="ColetaIniciada"/>).
    /// </summary>
    Armazenada
}
