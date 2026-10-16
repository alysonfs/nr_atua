using System.ComponentModel.DataAnnotations;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Configurações do comando de canal (ADR-042), seção <c>TriagemChannel</c>.
/// Validadas no startup (<c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c>
/// em <c>Program.cs</c>) — nenhum valor inválido é silenciosamente aceito.
/// </summary>
public sealed class TriagemChannelOptions
{
    public const string SectionName = "TriagemChannel";

    /// <summary>
    /// Duração da lease de claim, em segundos — após esse prazo, o comando
    /// volta a ser elegível para reivindicação sem alterar sua chave de
    /// idempotência (ADR-042).
    /// </summary>
    [Range(1, int.MaxValue)]
    public int LeaseSeconds { get; init; } = 60;
}
