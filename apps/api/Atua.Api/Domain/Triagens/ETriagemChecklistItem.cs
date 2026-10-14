namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Itens do checklist mínimo rígido da triagem (épico de triagem WhatsApp
/// Fase 1, pergunta 10 resolvida em 2026-10-13). Usado para contar tentativas
/// inválidas por item (máximo de 3 — pergunta 12 resolvida em 2026-10-13).
///
/// <see cref="PontoReferencia"/> e <see cref="Localizacao"/> são opcionais e
/// não bloqueiam a suficiência do checklist.
/// </summary>
public enum ETriagemChecklistItem
{
    DescricaoDefeito,
    NotaFiscal,
    Etiqueta,
    MidiaDefeito,
    Endereco,
    PontoReferencia,
    Localizacao,
}
