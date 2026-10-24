namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Indicador de garantia calculado na leitura a partir de
/// <see cref="Triagem.DataEmissaoNotaFiscal"/> e do prazo padrão configurado
/// em <c>TriagemLlm:GarantiaPadraoMeses</c> (decisão do PO: estimativa
/// provisória e global — hoje não existe prazo por produto nem por empresa).
/// Nunca persistido — somente o fato (data de emissão) é gravado; este
/// status é sempre recalculado, para nunca congelar a regra provisória em
/// dado gravado.
/// </summary>
public enum EWarrantyEstimateStatus
{
    /// <summary>Sem data de emissão da nota fiscal conhecida — não há base para estimar.</summary>
    Indeterminado,

    /// <summary>Data atual dentro do prazo padrão a partir da emissão.</summary>
    ProvavelmenteNaGarantia,

    /// <summary>Data atual além do prazo padrão a partir da emissão.</summary>
    ProvavelmenteForaDaGarantia,
}
