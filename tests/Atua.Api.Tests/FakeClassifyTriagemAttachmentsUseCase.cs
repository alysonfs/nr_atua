using Atua.Api.Application.Triagens;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Fake no-op de <see cref="IClassifyTriagemAttachmentsUseCase"/>, usado por
/// padrão em <c>TriagemLlmConversationServiceTests</c> (que não cobrem
/// anexos de imagem) — apenas registra as chamadas recebidas para que um
/// teste dedicado possa verificar que a classificação ocorre a cada turno,
/// antes da chamada de conversa.
/// </summary>
internal sealed class FakeClassifyTriagemAttachmentsUseCase : IClassifyTriagemAttachmentsUseCase
{
    public List<(Guid TenantId, Guid TriagemId)> Calls { get; } = [];

    public Task ClassifyPendingAsync(Guid tenantId, Triagem triagem, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((tenantId, triagem.Id));
        return Task.CompletedTask;
    }
}
