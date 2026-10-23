using Amazon.Runtime.Documents;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Converte o <c>Document</c> retornado pelo tool use do Bedrock Converse no
/// payload tipado do contrato, validando estritamente contra o esquema de
/// `triagem-llm-conducao.md` §2. Qualquer desvio (campo ausente, tipo
/// errado, confiança fora de [0,1], valor de enum desconhecido) resulta em
/// falha de parse — nunca uma exceção, e a saída nunca é aproveitada
/// parcialmente.
/// </summary>
internal static class TriagemLlmToolResultParser
{
    public static bool TryParse(Document input, out TriagemLlmSuccessPayload? payload, out string? error)
    {
        payload = null;
        error = null;

        if (!input.IsDictionary())
        {
            error = "entrada da ferramenta não é um objeto JSON";
            return false;
        }

        var root = input.AsDictionary();

        if (!TryGetNonEmptyString(root, "respostaAoConsumidor", out var resposta, out error))
        {
            return false;
        }

        if (!root.TryGetValue("camposEstruturados", out var camposDoc) || !camposDoc.IsDictionary())
        {
            error = "campo 'camposEstruturados' ausente ou não é um objeto";
            return false;
        }

        if (!TryParseCamposEstruturados(camposDoc.AsDictionary(), out var campos, out error))
        {
            return false;
        }

        if (!root.TryGetValue("itensAtendidos", out var itensDoc) || !itensDoc.IsList())
        {
            error = "campo 'itensAtendidos' ausente ou não é uma lista";
            return false;
        }

        if (!TryParseItensAtendidos(itensDoc.AsList(), out var itens, out error))
        {
            return false;
        }

        if (!root.TryGetValue("sinalDesvio", out var sinalDoc) || !sinalDoc.IsDictionary())
        {
            error = "campo 'sinalDesvio' ausente ou não é um objeto";
            return false;
        }

        if (!TryParseSinalDesvio(sinalDoc.AsDictionary(), out var sinal, out error))
        {
            return false;
        }

        payload = new TriagemLlmSuccessPayload(resposta!, campos!, itens!, sinal!);
        return true;
    }

    private static bool TryParseCamposEstruturados(
        Dictionary<string, Document> root,
        out TriagemLlmCamposEstruturados? campos,
        out string? error)
    {
        campos = null;

        if (!TryParseCampoOpcional(root, "tipoDefeito", out var tipoDefeito, out error) ||
            !TryParseCampoOpcional(root, "sintoma", out var sintoma, out error) ||
            !TryParseCampoOpcional(root, "codigoErro", out var codigoErro, out error) ||
            !TryParseCampoOpcional(root, "categoria", out var categoria, out error))
        {
            return false;
        }

        campos = new TriagemLlmCamposEstruturados(tipoDefeito, sintoma, codigoErro, categoria);
        return true;
    }

    private static bool TryParseCampoOpcional(
        Dictionary<string, Document> root,
        string propertyName,
        out TriagemLlmCampoEstruturado? campo,
        out string? error)
    {
        campo = null;
        error = null;

        if (!root.TryGetValue(propertyName, out var doc) || doc.IsNull())
        {
            return true;
        }

        if (!doc.IsDictionary())
        {
            error = $"campo '{propertyName}' não é um objeto";
            return false;
        }

        var obj = doc.AsDictionary();

        if (!TryGetNonEmptyString(obj, "valor", out var valor, out error))
        {
            return false;
        }

        if (!obj.TryGetValue("confianca", out var confiancaDoc) ||
            !(confiancaDoc.IsDouble() || confiancaDoc.IsLong() || confiancaDoc.IsInt()))
        {
            error = $"campo '{propertyName}.confianca' ausente ou não é numérico";
            return false;
        }

        var confianca = confiancaDoc.IsDouble() ? confiancaDoc.AsDouble() : confiancaDoc.AsLong();
        if (confianca is < 0 or > 1)
        {
            error = $"campo '{propertyName}.confianca' fora do intervalo [0,1]";
            return false;
        }

        campo = new TriagemLlmCampoEstruturado(valor!, confianca);
        return true;
    }

    private static bool TryParseItensAtendidos(
        List<Document> list,
        out IReadOnlyList<TriagemLlmItemAtendido>? itens,
        out string? error)
    {
        itens = null;
        error = null;
        var result = new List<TriagemLlmItemAtendido>(list.Count);

        foreach (var entryDoc in list)
        {
            if (!entryDoc.IsDictionary())
            {
                error = "entrada de 'itensAtendidos' não é um objeto";
                return false;
            }

            var entry = entryDoc.AsDictionary();

            if (!TryGetNonEmptyString(entry, "item", out var itemName, out error))
            {
                return false;
            }

            if (!Enum.TryParse<ETriagemChecklistItem>(itemName, out var item))
            {
                error = $"valor de 'item' desconhecido: '{itemName}'";
                return false;
            }

            if (!TryGetNonEmptyString(entry, "evidencia", out var evidencia, out error))
            {
                return false;
            }

            result.Add(new TriagemLlmItemAtendido(item, evidencia!));
        }

        itens = result;
        return true;
    }

    private static bool TryParseSinalDesvio(
        Dictionary<string, Document> root,
        out TriagemLlmSinalDesvio? sinal,
        out string? error)
    {
        sinal = null;
        error = null;

        if (!root.TryGetValue("detectado", out var detectadoDoc) || !detectadoDoc.IsBool())
        {
            error = "campo 'sinalDesvio.detectado' ausente ou não é booleano";
            return false;
        }

        string? motivo = null;
        if (root.TryGetValue("motivo", out var motivoDoc) && !motivoDoc.IsNull())
        {
            if (!motivoDoc.IsString())
            {
                error = "campo 'sinalDesvio.motivo' não é texto";
                return false;
            }

            motivo = motivoDoc.AsString();
        }

        sinal = new TriagemLlmSinalDesvio(detectadoDoc.AsBool(), motivo);
        return true;
    }

    private static bool TryGetNonEmptyString(
        Dictionary<string, Document> root,
        string propertyName,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;

        if (!root.TryGetValue(propertyName, out var doc) || !doc.IsString())
        {
            error = $"campo '{propertyName}' ausente ou não é texto";
            return false;
        }

        value = doc.AsString();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"campo '{propertyName}' é vazio";
            return false;
        }

        return true;
    }
}
