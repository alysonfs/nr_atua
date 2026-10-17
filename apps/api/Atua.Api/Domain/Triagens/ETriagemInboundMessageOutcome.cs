namespace Atua.Api.Domain.Triagens;

public enum ETriagemInboundMessageOutcome
{
    IgnoradaGrupo,
    IgnoradaPropria,
    IgnoradaVazia,
    SemTriagem,
    Ambigua,
    ColetaIniciada,
    JaEmColeta
}
