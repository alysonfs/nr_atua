using System.ComponentModel.DataAnnotations;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Configurações da drenagem da outbox do gatilho de triagem (ADR-041),
/// seção <c>TriagemOutbox</c>. Validadas no startup (<c>ValidateDataAnnotations</c>
/// + <c>ValidateOnStart</c> em <c>Program.cs</c>, mais <see cref="TriagemOutboxOptionsValidator"/>
/// para a regra entre campos) — nenhum valor inválido é silenciosamente
/// aceito.
/// </summary>
public sealed class TriagemOutboxOptions
{
    public const string SectionName = "TriagemOutbox";

    /// <summary>Intervalo entre ciclos de drenagem, em segundos.</summary>
    [Range(1, int.MaxValue)]
    public int PollingIntervalSeconds { get; init; } = 5;

    /// <summary>Quantidade máxima de linhas reivindicadas por ciclo.</summary>
    [Range(1, 1_000)]
    public int BatchSize { get; init; } = 20;

    /// <summary>Duração da lease de claim, em segundos — após esse prazo, a linha volta a ser elegível.</summary>
    [Range(1, int.MaxValue)]
    public int LeaseSeconds { get; init; } = 60;

    /// <summary>Número máximo de tentativas antes de transitar para <c>Failed</c>.</summary>
    [Range(1, int.MaxValue)]
    public int MaxAttempts { get; init; } = 10;

    /// <summary>Atraso base (1ª tentativa) do backoff exponencial, em segundos.</summary>
    [Range(1, int.MaxValue)]
    public int BaseRetrySeconds { get; init; } = 5;

    /// <summary>Teto do backoff exponencial, em segundos.</summary>
    [Range(1, int.MaxValue)]
    public int MaxRetrySeconds { get; init; } = 300;
}
