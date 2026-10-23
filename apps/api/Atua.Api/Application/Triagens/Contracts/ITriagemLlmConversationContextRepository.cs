using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Monta a entrada minimizada da LLM (plano M2, §3; `triagem-llm-conducao.md`
/// §2) a partir de dados já persistidos da triagem — nunca inclui CPF,
/// telefone, endereço completo ou nome do consumidor (apenas produto/marca
/// da OS e o texto já trocado na conversa). Separado de
/// <see cref="ITriagemLlmClient"/> porque é dado interno da Master API, não
/// parte do contrato do fornecedor de LLM.
/// </summary>
public interface ITriagemLlmConversationContextRepository
{
    Task<TriagemLlmConversationContext> LoadAsync(Guid tenantId, Guid triagemId, Guid workOrderId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Contexto carregado para montar <see cref="TriagemLlmRequest"/>.
/// <see cref="JaApresentouIdentidade"/> é <c>true</c> quando já existe ao
/// menos uma resposta conduzida pela LLM nesta triagem
/// (<see cref="ETriagemChannelCommandKind.RespostaLlm"/>) — evita repetir a
/// apresentação de identidade a cada turno
/// (`triagem-llm-identidade-tom.md`).
/// </summary>
public sealed record TriagemLlmConversationContext(
    string TenantNomeComercial,
    string Produto,
    string? Marca,
    bool JaApresentouIdentidade,
    IReadOnlyList<TriagemLlmConversationTurn> HistoricoConversa,
    IReadOnlyList<TriagemLlmAttachmentMetadata> AnexosRecebidos);
