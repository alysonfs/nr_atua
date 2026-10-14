namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Lançada quando uma operação tenta transicionar a triagem para um estado não
/// permitido a partir do estado atual, ou quando uma operação exige um estado
/// atual diferente do real (ex.: registrar item de checklist fora de
/// <see cref="ETriagemStatus.EmColeta"/>). O estado da triagem não é alterado.
/// </summary>
public sealed class InvalidTriagemTransitionException : Exception
{
    public InvalidTriagemTransitionException(ETriagemStatus estadoAtual, string operacao)
        : base($"Operação '{operacao}' não é permitida no estado atual '{estadoAtual}' da triagem.")
    {
        EstadoAtual = estadoAtual;
        Operacao = operacao;
    }

    /// <summary>Estado em que a triagem permaneceu após a tentativa rejeitada.</summary>
    public ETriagemStatus EstadoAtual { get; }

    /// <summary>Nome da operação que foi rejeitada.</summary>
    public string Operacao { get; }
}
