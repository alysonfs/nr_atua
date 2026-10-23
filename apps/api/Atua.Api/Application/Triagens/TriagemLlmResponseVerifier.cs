using System.Text.RegularExpressions;
using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação determinística de <see cref="ITriagemLlmResponseVerifier"/>
/// (`triagem-llm-conducao.md` §2, plano §5.3). Bloqueia:
///
/// - promessa de agendamento, cancelamento ou conclusão de OS;
/// - promessa de prazo, preço/orçamento ou garantia;
/// - links (a LLM nunca deve enviar URLs ao consumidor);
/// - pedido de dados fora do checklist (CPF, senha, dados de cartão);
/// - texto vazio ou maior que <see cref="MaxLength"/>.
///
/// As expressões são verificadas em texto normalizado (minúsculas, sem
/// acento) — detecção por palavra-chave, não por NLP. Falsos positivos
/// pontuais são aceitáveis: o efeito de um bloqueio indevido é cair para a
/// pergunta de fallback pré-aprovada (`triagem-llm-conducao.md` §2), nunca
/// enviar uma mensagem arriscada ao consumidor.
/// </summary>
public sealed class TriagemLlmResponseVerifier : ITriagemLlmResponseVerifier
{
    /// <summary>Limite de tamanho da mensagem — mesma ordem de grandeza de um template de WhatsApp (ver TriagemMessageTemplateDefaults).</summary>
    public const int MaxLength = 1_200;

    private static readonly (string Motivo, Regex Padrao)[] Bloqueios =
    [
        ("promessa de agendamento",
            new Regex(@"\b(agend(ar|amento|aremos|arei|ado)|marc(ar|aremos)\s+(a\s+)?visita|hor[aá]rio\s+da\s+visita)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("promessa de cancelamento",
            new Regex(@"\bcancel(ar|amento|aremos|arei|ada|ado)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("promessa de conclusão da OS",
            new Regex(@"\b(conclu(ir|[ií]da|[ií]do|iremos)|encerra(r|remos)|finaliza(r|remos))\s+(a\s+)?(os|ordem\s+de\s+servi[cç]o)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("promessa de prazo",
            new Regex(@"\bprazo\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("promessa de preço/orçamento",
            new Regex(@"(\bor[cç]amento\b|\bpre[cç]o\b|\bvalor\s+(do|da)\s+(servi[cç]o|visita|conserto)\b|R\$\s?\d)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("promessa de garantia",
            new Regex(@"\bgarantia\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("link",
            new Regex(@"(https?://|www\.)\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("pedido de CPF",
            new Regex(@"\bcpf\b|\d{3}\.?\d{3}\.?\d{3}-?\d{2}", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("pedido de senha",
            new Regex(@"\bsenha\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("pedido de dados de cartão",
            new Regex(@"\bcart[aã]o\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    ];

    public TriagemLlmVerificationResult Verify(string respostaAoConsumidor)
    {
        if (string.IsNullOrWhiteSpace(respostaAoConsumidor))
        {
            return TriagemLlmVerificationResult.Bloqueado("resposta vazia");
        }

        if (respostaAoConsumidor.Length > MaxLength)
        {
            return TriagemLlmVerificationResult.Bloqueado($"resposta excede o limite de {MaxLength} caracteres");
        }

        foreach (var (motivo, padrao) in Bloqueios)
        {
            if (padrao.IsMatch(respostaAoConsumidor))
            {
                return TriagemLlmVerificationResult.Bloqueado(motivo);
            }
        }

        return TriagemLlmVerificationResult.Ok;
    }
}
