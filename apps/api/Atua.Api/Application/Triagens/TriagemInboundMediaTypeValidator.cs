using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Valida se um arquivo de mídia inbound é aceito: tipo declarado compatível
/// com a lista de <c>contentType</c> permitidos para o <c>mediaKind</c> e
/// assinatura binária (magic bytes) compatível com o <c>contentType</c>
/// declarado. Figurinhas e documentos além de PDF nunca são aceitos — não há
/// <c>mediaKind</c> para eles (decisão do PO, 2026-10-21).
///
/// Validação técnica — não decide regra de negócio, apenas se o arquivo é
/// tecnicamente aceitável para armazenamento.
/// </summary>
public static class TriagemInboundMediaTypeValidator
{
    private static readonly IReadOnlyDictionary<ETriagemInboundMediaKind, string[]> AllowedContentTypes =
        new Dictionary<ETriagemInboundMediaKind, string[]>
        {
            [ETriagemInboundMediaKind.Audio] =
                ["audio/ogg", "audio/mpeg", "audio/mp4", "audio/aac", "audio/amr", "audio/webm"],
            [ETriagemInboundMediaKind.Image] = ["image/jpeg", "image/png", "image/webp"],
            [ETriagemInboundMediaKind.Video] = ["video/mp4", "video/3gpp"],
            [ETriagemInboundMediaKind.Document] = ["application/pdf"],
        };

    public static bool IsAccepted(ETriagemInboundMediaKind mediaKind, string contentType, ReadOnlySpan<byte> content)
    {
        if (!AllowedContentTypes.TryGetValue(mediaKind, out var allowed)
            || !allowed.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchesSignature(contentType, content);
    }

    private static bool MatchesSignature(string contentType, ReadOnlySpan<byte> content) => contentType switch
    {
        "application/pdf" => StartsWith(content, "%PDF-"u8),
        "image/jpeg" => content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF,
        "image/png" => StartsWith(content, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/webp" => content.Length >= 12 && StartsWith(content, "RIFF"u8) &&
            content[8..12].SequenceEqual("WEBP"u8),
        "video/mp4" or "video/3gpp" => content.Length >= 8 && content[4..8].SequenceEqual("ftyp"u8),
        "audio/mp4" => content.Length >= 8 && content[4..8].SequenceEqual("ftyp"u8),
        "audio/mpeg" => StartsWith(content, "ID3"u8)
            || (content.Length >= 2 && content[0] == 0xFF && (content[1] & 0xE0) == 0xE0),
        "audio/ogg" => StartsWith(content, "OggS"u8),
        "audio/amr" => StartsWith(content, "#!AMR"u8),
        "audio/aac" => content.Length >= 2 && content[0] == 0xFF && (content[1] & 0xF0) == 0xF0,
        "audio/webm" => StartsWith(content, [0x1A, 0x45, 0xDF, 0xA3]),
        _ => false,
    };

    private static bool StartsWith(ReadOnlySpan<byte> content, ReadOnlySpan<byte> prefix) =>
        content.Length >= prefix.Length && content[..prefix.Length].SequenceEqual(prefix);
}
