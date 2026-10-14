namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Estados da triagem automatizada de OS via WhatsApp (épico de triagem WhatsApp
/// Fase 1, seção 5 — máquina de estados). Autoridade da máquina de estados é da
/// Master API (arquitetura, seção 4).
///
/// Total: 9 estados. <see cref="Expirada"/> e <see cref="AguardandoIntervencao"/>
/// são caminhos independentes — uma triagem expirada não transita
/// automaticamente para <see cref="AguardandoIntervencao"/>.
/// </summary>
public enum ETriagemStatus
{
    /// <summary>OS enfileirada, template ainda não enviado.</summary>
    AguardandoEnvio,

    /// <summary>Template enviado, aguardando o consumidor.</summary>
    ContatoIniciado,

    /// <summary>Conversa ativa com a LLM, itens pendentes.</summary>
    EmColeta,

    /// <summary>A LLM considera o checklist mínimo completo.</summary>
    InformacoesSuficientes,

    /// <summary>Disponível aos operadores autorizados do pool compartilhado.</summary>
    LiberadaAoPool,

    /// <summary>O humano assumiu. Estado terminal da triagem.</summary>
    AssumidaPorOperador,

    /// <summary>Prazo de 48h sem checklist mínimo; encaminhada ao pool para decisão humana.</summary>
    Expirada,

    /// <summary>Número inválido, sem WhatsApp, ou falha definitiva do envio inicial.</summary>
    FalhaDeContato,

    /// <summary>
    /// Mídia ilegível após 3 tentativas do mesmo item, ou mensagens fora de
    /// contexto após 3 desvios consecutivos. LLM para neste atendimento.
    /// </summary>
    AguardandoIntervencao,
}
