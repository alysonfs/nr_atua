using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Caso de uso idempotente de início da triagem (ADR-039, seção "Decisão
/// proposta", itens 1 e 2; arquitetura, seção 6): registra o aceite do
/// gatilho de início usando uma chave estável por tenant, integração e OS, e
/// em seguida emite o comando <c>SendTemplate</c> ao canal (arquitetura,
/// seção 5.3), persistindo o resultado estruturado como transição de
/// estado.
///
/// Reentregas do mesmo gatilho (mesmo tenant + integração + OS) retomam o
/// envio quando a triagem já registrada ainda estiver
/// <see cref="ETriagemStatus.AguardandoEnvio"/>, e curto-circuitam (sem
/// repetir a chamada ao canal) quando o estado já avançou além do envio
/// inicial — nunca recriam nem resetam estado já avançado.
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
    ITriagemChannel channel,
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

        // ADR-039: reentrega da mesma chave retorna/retoma o resultado
        // conhecido. A elegibilidade `assigned` é exigida apenas antes de
        // CRIAR, nunca antes de retornar uma triagem já registrada — por
        // isso a OS não é reconsultada/rejeitada por status atual quando já
        // existe triagem para a chave.
        var existing = await triagemRepository.FindByKeyAsync(key, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status != ETriagemStatus.AguardandoEnvio)
            {
                // Curto-circuito: o estado já avançou além do envio inicial
                // — nunca repete o comando SendTemplate ao canal (arquitetura,
                // seção 6).
                return StartTriagemResult.Existing(existing.Id, existing.Status);
            }

            var resumedStatus = await SendTemplateAndPersistAsync(existing, key, cancellationToken);
            return StartTriagemResult.Existing(existing.Id, resumedStatus);
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

        if (!result.Created && result.Triagem.Status != ETriagemStatus.AguardandoEnvio)
        {
            // Perdeu a corrida de GetOrAddAsync e o vencedor já avançou além
            // do envio inicial — curto-circuito, nenhuma chamada ao canal.
            return StartTriagemResult.Existing(result.Triagem.Id, result.Triagem.Status);
        }

        var status = await SendTemplateAndPersistAsync(result.Triagem, key, cancellationToken);

        return result.Created
            ? StartTriagemResult.Created(result.Triagem.Id, status)
            : StartTriagemResult.Existing(result.Triagem.Id, status);
    }

    /// <summary>
    /// Emite o comando <c>SendTemplate</c> ao canal e persiste o resultado
    /// como transição de estado. Nenhuma transação PostgreSQL fica aberta
    /// durante a chamada ao canal: a persistência do <c>GetOrAddAsync</c>
    /// (ou a leitura de <c>FindByKeyAsync</c>) já foi concluída/commitada
    /// antes desta chamada, e a transição resultante é persistida em uma
    /// nova chamada a <c>SaveChangesAsync</c> somente depois que o canal
    /// responde.
    /// </summary>
    private async Task<ETriagemStatus> SendTemplateAndPersistAsync(Triagem triagem, TriagemIdempotencyKey key,
        CancellationToken cancellationToken)
    {
        var command = new TriagemSendTemplateCommand(
            triagem.TenantId,
            triagem.IntegrationId,
            triagem.Id,
            triagem.WorkOrderId,
            TriagemChannelTemplates.InitialContactTemplateId,
            TriagemChannelTemplates.InitialContactTemplateVersion,
            key.ToString(),
            triagem.Id);

        var sendResult = await channel.SendTemplateAsync(command, cancellationToken);

        var now = timeProvider.GetUtcNow();

        switch (sendResult.Outcome)
        {
            case ETriagemChannelSendOutcome.Success:
                triagem.MarcarContatoIniciado(now);
                break;

            case ETriagemChannelSendOutcome.PermanentFailure:
                triagem.MarcarFalhaDeContato(now);
                break;

            case ETriagemChannelSendOutcome.TransientFailure:
                // Preserva AguardandoEnvio e a mesma chave de idempotência
                // (ADR-039, item 7) — nenhuma transição é persistida aqui.
                // A outbox (ADR-041) classifica esta exceção e reagenda
                // retentativa com backoff.
                throw new TriagemChannelTransientFailureException(triagem.Id, sendResult.ErrorCode,
                    sendResult.ErrorDescription);

            default:
                throw new InvalidOperationException($"Outcome de canal não tratado: {sendResult.Outcome}.");
        }

        try
        {
            await triagemRepository.SaveTransitionAsync(triagem, cancellationToken);
            return triagem.Status;
        }
        catch (TriagemConcurrencyConflictException)
        {
            // Outro processo já persistiu uma transição concorrente para
            // esta mesma triagem (ex.: corrida de GetOrAddAsync resolvida
            // para o mesmo registro por dois chamadores, cada um enviando o
            // template). Nunca repete a chamada ao canal neste branch —
            // apenas recarrega o estado vencedor já persistido.
            var winner = await triagemRepository.FindByKeyAsync(key, cancellationToken);
            if (winner is null)
            {
                throw new InvalidOperationException(
                    $"Conflito de concorrência detectado para a triagem '{triagem.Id}', mas nenhum registro foi " +
                    $"encontrado para a chave '{key}'.");
            }

            return winner.Status;
        }
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
