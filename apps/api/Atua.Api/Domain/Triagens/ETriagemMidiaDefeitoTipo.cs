namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Tipo de mídia do defeito recebida do consumidor. O checklist mínimo exige
/// pelo menos uma foto OU vídeo do defeito (épico de triagem WhatsApp Fase 1,
/// seção 4 e pergunta 10 resolvida em 2026-10-13).
/// </summary>
public enum ETriagemMidiaDefeitoTipo
{
    Foto,
    Video,
}
