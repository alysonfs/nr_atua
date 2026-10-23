namespace Atua.Api.Application.Triagens;

/// <summary>
/// Feature flag e allowlist do loop conversacional da LLM dentro da Master
/// API (plano M2, docs/implementation/PLANO-triagem-llm-jev.md §3/§10).
/// Vinculada à mesma seção de configuração <c>TriagemLlm</c> de
/// <see cref="Infrastructure.Llm.TriagemLlmOptions"/> (que configura o
/// adaptador Bedrock) — ambas coexistem na mesma seção porque regem a
/// mesma funcionalidade, em camadas diferentes (Application x
/// Infrastructure).
///
/// Elegibilidade: AND entre <see cref="AllowedTenantIds"/> e
/// <see cref="AllowedPhones"/> — ambas vazias (padrão) significa que
/// nenhuma triagem é elegível, mesmo com <see cref="ConversationEnabled"/>
/// ligado (fail-closed por padrão, conforme pedido explicitamente: "fora da
/// allowlist, comportamento atual inalterado").
/// </summary>
public sealed class TriagemLlmConversationOptions
{
    public const string SectionName = "TriagemLlm";

    /// <summary>Liga o loop conversacional da LLM na ingestão de mensagens inbound (padrão: desligado).</summary>
    public bool ConversationEnabled { get; init; }

    /// <summary>Tenants elegíveis ao loop conversacional. Vazio (padrão) = nenhum tenant elegível.</summary>
    public Guid[] AllowedTenantIds { get; init; } = [];

    /// <summary>
    /// Telefones (consumidor) elegíveis ao loop conversacional, no formato
    /// E.164 (ex.: <c>+5584994241248</c>). Vazio (padrão) = nenhum telefone
    /// elegível.
    /// </summary>
    public string[] AllowedPhones { get; init; } = [];

    /// <summary>
    /// Máximo de chamadas à LLM por triagem (orçamento de turnos, plano
    /// M2 §3/§10). Ao exceder, a triagem é movida para
    /// <see cref="Domain.Triagens.ETriagemStatus.AguardandoIntervencao"/> em
    /// vez de continuar chamando o fornecedor.
    /// </summary>
    public int MaxLlmCallsPerTriagem { get; init; } = 20;

    /// <summary>
    /// Novas tentativas (além da primeira) quando o fornecedor devolve
    /// <see cref="Domain.Triagens.ETriagemLlmOutcome.ProviderUnavailable"/>
    /// (decisão do PO, 2026-10-23). Padrão 2 — total de até 3 chamadas por
    /// turno antes de mover a triagem para <c>AguardandoIntervencao</c>.
    /// Cada tentativa (inclusive as que falham) é registrada em
    /// <see cref="Contracts.ITriagemLlmUsageRepository"/> e conta para
    /// <see cref="MaxLlmCallsPerTriagem"/>.
    /// </summary>
    public int MaxProviderRetries { get; init; } = 2;

    /// <summary>
    /// Base (em milissegundos) do backoff exponencial entre tentativas:
    /// a N-ésima tentativa (1-based) espera <c>RetryBackoffBaseMs * 2^(N-1)</c>
    /// antes da tentativa seguinte. Padrão 1000ms produz 1s antes da 2ª
    /// tentativa e 2s antes da 3ª (decisão do PO, 2026-10-23).
    /// </summary>
    public int RetryBackoffBaseMs { get; init; } = 1000;

    /// <summary>
    /// Verifica se a triagem informada está dentro do escopo restrito do
    /// loop conversacional (plano M2 — "restrito a número de teste").
    /// Compara telefones só pelos dígitos (mesma normalização de
    /// <c>TriagemInboundMessageRepository.NormalizePhone</c>) — tolera
    /// variação de formatação (com/sem "+", DDI) entre a allowlist
    /// configurada e o telefone persistido.
    /// </summary>
    public bool IsEligible(Guid tenantId, string consumerPhone) =>
        ConversationEnabled
        && AllowedTenantIds.Contains(tenantId)
        && AllowedPhones.Any(allowed => ToPhoneMatchKey(allowed) == ToPhoneMatchKey(consumerPhone));

    // O WhatsApp entrega celulares brasileiros sem o nono dígito
    // (55 + DDD + 8 dígitos); mesma regra de TriagemInboundMessageRepository.
    private static string ToPhoneMatchKey(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 13 && digits.StartsWith("55", StringComparison.Ordinal) && digits[4] == '9'
            ? digits.Remove(4, 1)
            : digits;
    }
}
