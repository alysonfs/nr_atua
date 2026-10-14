using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Caso de uso idempotente de início da triagem (ADR-039, seção "Decisão
/// proposta", itens 1 e 2; arquitetura, seção 6): registra o aceite do
/// gatilho de início usando uma chave estável por tenant, integração e OS,
/// antes de qualquer efeito externo (envio do template, fila, canal), que
/// ficam para incrementos futuros.
///
/// Reentregas do mesmo gatilho (mesmo tenant + integração + OS) retornam a
/// triagem já registrada, sem recriar nem resetar estado já avançado.
/// </summary>
public sealed class StartTriagemUseCase(ITriagemRepository triagemRepository, TimeProvider timeProvider)
{
    public async Task<StartTriagemResult> ExecuteAsync(Guid tenantId, Guid integrationId, Guid workOrderId,
        string workOrderProviderId, CancellationToken cancellationToken)
    {
        if (workOrderId == Guid.Empty)
        {
            throw new ArgumentException("WorkOrderId não pode ser vazio.", nameof(workOrderId));
        }

        // Validações de tenant, integração e OS externa são responsabilidade
        // de TriagemIdempotencyKey (ADR-039).
        var key = new TriagemIdempotencyKey(tenantId, integrationId, workOrderProviderId);

        var now = timeProvider.GetUtcNow();
        var candidate = new Triagem(Guid.CreateVersion7(), tenantId, integrationId, workOrderId, now);

        var result = await triagemRepository.GetOrAddAsync(key, candidate, cancellationToken);

        return new StartTriagemResult(result.Triagem.Id, result.Triagem.Status, result.Created);
    }
}

/// <summary>Resultado do início idempotente da triagem.</summary>
/// <param name="TriagemId">Id da triagem (nova ou já existente para a chave).</param>
/// <param name="Status">Status atual da triagem — pode estar além de <see cref="ETriagemStatus.AguardandoEnvio"/> em reentregas tardias.</param>
/// <param name="Created">
/// <c>true</c> quando esta chamada criou a triagem; <c>false</c> quando já
/// existia um registro para a chave (reentrega).
/// </param>
public sealed record StartTriagemResult(Guid TriagemId, ETriagemStatus Status, bool Created);
