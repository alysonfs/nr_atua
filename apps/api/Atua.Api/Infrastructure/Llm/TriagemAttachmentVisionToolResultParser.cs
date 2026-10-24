using Amazon.Runtime.Documents;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Converte o <c>Document</c> retornado pelo tool use do Bedrock Converse no
/// payload tipado de <see cref="TriagemAttachmentVisionPayload"/>, validando
/// estritamente contra o esquema de <see cref="TriagemAttachmentVisionToolSchema"/>.
/// Qualquer desvio resulta em falha de parse — nunca uma exceção, e a saída
/// nunca é aproveitada parcialmente. Mesmo padrão de
/// <see cref="TriagemLlmToolResultParser"/>.
/// </summary>
internal static class TriagemAttachmentVisionToolResultParser
{
    public static bool TryParse(Document input, out TriagemAttachmentVisionPayload? payload, out string? error)
    {
        payload = null;
        error = null;

        if (!input.IsDictionary())
        {
            error = "entrada da ferramenta não é um objeto JSON";
            return false;
        }

        var root = input.AsDictionary();

        if (!root.TryGetValue("tipo", out var tipoDoc) || !tipoDoc.IsString())
        {
            error = "campo 'tipo' ausente ou não é texto";
            return false;
        }

        if (!Enum.TryParse<ETriagemAttachmentVisionTipo>(tipoDoc.AsString(), out var tipo))
        {
            error = $"valor de 'tipo' desconhecido: '{tipoDoc.AsString()}'";
            return false;
        }

        if (!root.TryGetValue("legivel", out var legivelDoc) || !legivelDoc.IsBool())
        {
            error = "campo 'legivel' ausente ou não é booleano";
            return false;
        }

        if (!root.TryGetValue("confianca", out var confiancaDoc) ||
            !(confiancaDoc.IsDouble() || confiancaDoc.IsLong() || confiancaDoc.IsInt()))
        {
            error = "campo 'confianca' ausente ou não é numérico";
            return false;
        }

        var confianca = confiancaDoc.IsDouble() ? confiancaDoc.AsDouble() : confiancaDoc.AsLong();
        if (confianca is < 0 or > 1)
        {
            error = "campo 'confianca' fora do intervalo [0,1]";
            return false;
        }

        string? modelo = null;
        if (root.TryGetValue("modelo", out var modeloDoc) && !modeloDoc.IsNull())
        {
            if (!modeloDoc.IsString())
            {
                error = "campo 'modelo' não é texto";
                return false;
            }

            modelo = modeloDoc.AsString();
        }

        string? numeroSerie = null;
        if (root.TryGetValue("numeroSerie", out var numeroSerieDoc) && !numeroSerieDoc.IsNull())
        {
            if (!numeroSerieDoc.IsString())
            {
                error = "campo 'numeroSerie' não é texto";
                return false;
            }

            numeroSerie = numeroSerieDoc.AsString();
        }

        string? numeroNotaFiscal = null;
        if (root.TryGetValue("numeroNotaFiscal", out var numeroNotaFiscalDoc) && !numeroNotaFiscalDoc.IsNull())
        {
            if (!numeroNotaFiscalDoc.IsString())
            {
                error = "campo 'numeroNotaFiscal' não é texto";
                return false;
            }

            numeroNotaFiscal = numeroNotaFiscalDoc.AsString();
        }

        string? serieNotaFiscal = null;
        if (root.TryGetValue("serieNotaFiscal", out var serieNotaFiscalDoc) && !serieNotaFiscalDoc.IsNull())
        {
            if (!serieNotaFiscalDoc.IsString())
            {
                error = "campo 'serieNotaFiscal' não é texto";
                return false;
            }

            serieNotaFiscal = serieNotaFiscalDoc.AsString();
        }

        DateTimeOffset? dataEmissaoNotaFiscal = null;
        if (root.TryGetValue("dataEmissaoNotaFiscal", out var dataEmissaoDoc) && !dataEmissaoDoc.IsNull())
        {
            if (!dataEmissaoDoc.IsString())
            {
                error = "campo 'dataEmissaoNotaFiscal' não é texto";
                return false;
            }

            if (!DateOnly.TryParse(dataEmissaoDoc.AsString(), out var dataEmissao))
            {
                error = $"campo 'dataEmissaoNotaFiscal' em formato inválido: '{dataEmissaoDoc.AsString()}'";
                return false;
            }

            dataEmissaoNotaFiscal = new DateTimeOffset(dataEmissao.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        payload = new TriagemAttachmentVisionPayload(
            tipo,
            legivelDoc.AsBool(),
            string.IsNullOrWhiteSpace(modelo) ? null : modelo,
            string.IsNullOrWhiteSpace(numeroSerie) ? null : numeroSerie,
            confianca,
            string.IsNullOrWhiteSpace(numeroNotaFiscal) ? null : numeroNotaFiscal,
            string.IsNullOrWhiteSpace(serieNotaFiscal) ? null : serieNotaFiscal,
            dataEmissaoNotaFiscal);
        return true;
    }
}
