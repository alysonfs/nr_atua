using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Tests;

/// <summary>
/// Fake mínimo de <see cref="ITriagemLlmConversationContextRepository"/>
/// (plano M2) para testes que não exercitam o loop conversacional (flag
/// desligada/allowlist vazia por padrão) — nunca é chamado nesse cenário,
/// mas precisa estar registrado para resolver <c>TriagemLlmConversationService</c>.
/// </summary>
internal sealed class FakeTriagemLlmConversationContextRepository : ITriagemLlmConversationContextRepository
{
    public TriagemLlmConversationContext Context { get; set; } = new(
        TenantNomeComercial: "Tenant de Teste",
        Produto: "Produto de Teste",
        Marca: null,
        JaApresentouIdentidade: false,
        HistoricoConversa: [],
        AnexosRecebidos: []);

    public Task<TriagemLlmConversationContext> LoadAsync(Guid tenantId, Guid triagemId, Guid workOrderId,
        CancellationToken cancellationToken) => Task.FromResult(Context);
}
