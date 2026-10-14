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
///
/// Diferente do incremento anterior, o <c>WorkOrderId</c> interno nunca é
/// recebido do chamador: é sempre resolvido internamente, consultando
/// autoritativamente a OS por tenant + integração + <c>WorkOrderProviderId</c>
/// externo (arquitetura, seção 3, itens 2/3). Isso impede que um chamador
/// referencie uma OS de outro tenant/integração ou uma OS inelegível.
/// </summary>
public sealed class StartTriagemUseCase(
    ITriagemEligibleWorkOrderQuery workOrderQuery,
    ITriagemRepository triagemRepository,
    TimeProvider timeProvider)
{
    public async Task<StartTriagemResult> ExecuteAsync(Guid tenantId, Guid integrationId,
        string workOrderProviderId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        }

        if (integrationId == Guid.Empty)
        {
            throw new ArgumentException("IntegrationId não pode ser vazio.", nameof(integrationId));
        }

        if (string.IsNullOrWhiteSpace(workOrderProviderId))
        {
            throw new ArgumentException("WorkOrderProviderId não pode ser vazio.", nameof(workOrderProviderId));
        }

        // Validações de tenant, integração e OS externa são responsabilidade
        // de TriagemIdempotencyKey (ADR-039).
        var key = new TriagemIdempotencyKey(tenantId, integrationId, workOrderProviderId);

        // ADR-039: reentrega da mesma chave retorna o resultado conhecido.
        // A elegibilidade `assigned` é exigida apenas antes de CRIAR, nunca
        // antes de retornar uma triagem já registrada — por isso a OS não é
        // reconsultada/rejeitada por status atual quando já existe triagem
        // para a chave.
        var existing = await triagemRepository.FindByKeyAsync(key, cancellationToken);
        if (existing is not null)
        {
            return StartTriagemResult.Existing(existing.Id, existing.Status);
        }

        var workOrder = await workOrderQuery.ExecuteAsync(tenantId, integrationId, workOrderProviderId,
            cancellationToken);
        if (workOrder is null)
        {
            return StartTriagemResult.WorkOrderNotFound();
        }

        if (!TriagemEligibleWorkOrderStatus.IsEligible(workOrder.Status))
        {
            return StartTriagemResult.WorkOrderNotEligible();
        }

        var now = timeProvider.GetUtcNow();
        var candidate = new Triagem(Guid.CreateVersion7(), tenantId, integrationId, workOrder.WorkOrderId, now);

        var result = await triagemRepository.GetOrAddAsync(key, candidate, cancellationToken);

        return result.Created
            ? StartTriagemResult.Created(result.Triagem.Id, result.Triagem.Status)
            : StartTriagemResult.Existing(result.Triagem.Id, result.Triagem.Status);
    }
}

/// <summary>Resultado do início idempotente da triagem.</summary>
/// <param name="Outcome">Resultado tipado — ver <see cref="EStartTriagemOutcome"/>.</param>
/// <param name="TriagemId">
/// Id da triagem (nova ou já existente para a chave). <c>null</c> quando
/// <paramref name="Outcome"/> é <see cref="EStartTriagemOutcome.WorkOrderNotFound"/>
/// ou <see cref="EStartTriagemOutcome.WorkOrderNotEligible"/> — nenhuma
/// triagem é criada nesses casos.
/// </param>
/// <param name="Status">
/// Status atual da triagem — pode estar além de <see cref="ETriagemStatus.AguardandoEnvio"/>
/// em reentregas tardias. <c>null</c> quando nenhuma triagem existe (ver <paramref name="TriagemId"/>).
/// </param>
public sealed record StartTriagemResult(EStartTriagemOutcome Outcome, Guid? TriagemId, ETriagemStatus? Status)
{
    public static StartTriagemResult Created(Guid triagemId, ETriagemStatus status) =>
        new(EStartTriagemOutcome.Created, triagemId, status);

    public static StartTriagemResult Existing(Guid triagemId, ETriagemStatus status) =>
        new(EStartTriagemOutcome.Existing, triagemId, status);

    public static StartTriagemResult WorkOrderNotFound() =>
        new(EStartTriagemOutcome.WorkOrderNotFound, null, null);

    public static StartTriagemResult WorkOrderNotEligible() =>
        new(EStartTriagemOutcome.WorkOrderNotEligible, null, null);
}
