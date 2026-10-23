namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Registro de uso de LLM por chamada, por tenant e por triagem (ADR-048).
/// Base da cobrança por tenant e da auditoria exigida por
/// `triagem-llm-conducao.md` §5: "toda chamada registra triagem, fornecedor
/// e modelo, tokens de entrada e saída, latência [e] resultado da
/// validação". Não contém conteúdo de mídia nem texto livre do consumidor —
/// apenas metadados e contagens, mesmo princípio já aplicado aos logs de
/// mídia (ADR-047).
/// </summary>
public sealed class TriagemLlmUsage
{
    private TriagemLlmUsage() { }

    public TriagemLlmUsage(
        Guid id,
        Guid tenantId,
        Guid triagemId,
        string modelId,
        int inputTokens,
        int outputTokens,
        long latencyMs,
        ETriagemLlmOutcome outcome,
        DateTimeOffset createdAtUtc)
    {
        if (inputTokens < 0) throw new ArgumentOutOfRangeException(nameof(inputTokens));
        if (outputTokens < 0) throw new ArgumentOutOfRangeException(nameof(outputTokens));
        if (latencyMs < 0) throw new ArgumentOutOfRangeException(nameof(latencyMs));

        Id = id;
        TenantId = tenantId;
        TriagemId = triagemId;
        ModelId = modelId;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        LatencyMs = latencyMs;
        Outcome = outcome;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TriagemId { get; private set; }
    public string ModelId { get; private set; } = null!;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public long LatencyMs { get; private set; }
    public ETriagemLlmOutcome Outcome { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
