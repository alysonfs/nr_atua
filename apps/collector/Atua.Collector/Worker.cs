using Atua.Collector.Api;
using Atua.Collector.Configuration;
using Atua.Collector.Contracts;
using Atua.Collector.IService;
using Microsoft.Extensions.Options;

namespace Atua.Collector;

/// <summary>
/// Worker principal do Agente Coletor (RF-009).
///
/// Loop:
/// 1. Chama ClaimAsync — se 204, aguarda PollingIntervalSeconds e tenta novamente.
/// 2. Se há comando, executa a coleta via IIServiceCollector.
/// 3. Em sucesso, registra CompleteAsync(Succeeded).
/// 4. Em falha, classifica a exceção e registra CompleteAsync(Failed) com o motivo correto.
/// </summary>
public sealed class Worker(
    ICollectorApiClient apiClient,
    IIServiceCollector iServiceCollector,
    IOptions<CollectorWorkerOptions> options,
    ILogger<Worker> logger)
    : BackgroundService
{
    private readonly CollectorWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[WORKER] Agente Coletor iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Encerramento solicitado — sai do loop normalmente.
                break;
            }
            catch (Exception ex)
            {
                // Exceção inesperada no ciclo que não foi tratada internamente.
                // Loga e aguarda antes de tentar novamente para evitar loop frenético.
                logger.LogError(ex, "[WORKER] Exceção não tratada no ciclo principal. Aguardando antes de reiniciar.");
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
            }
        }

        logger.LogInformation("[WORKER] Agente Coletor encerrado.");
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        // 1. Tenta reivindicar um comando
        var command = await apiClient.ClaimAsync(cancellationToken);

        if (command is null)
        {
            // 204 — sem trabalho disponível; aguarda e tenta no próximo ciclo
            logger.LogDebug("[WORKER] Nenhum comando disponível. Aguardando {Seconds}s.", _options.PollingIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), cancellationToken);
            return;
        }

        // 2. Verifica elegibilidade do tenant ANTES de acessar o iService (RF-009.2/RN-009.2).
        // Falha na chamada de elegibilidade é tratada como IServiceUnavailable (indisponibilidade de rede/API).
        EligibilityResponse eligibility;
        try
        {
            eligibility = await apiClient.CheckEligibilityAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[WORKER] Verificação de elegibilidade cancelada para CommandId={CommandId}.", command.CommandId);
            await SafeCompleteAsync(command.CommandId, "Cancelled", null);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[WORKER] Falha ao verificar elegibilidade para CommandId={CommandId}.", command.CommandId);
            await SafeCompleteAsync(command.CommandId, "Failed", ECommandFailureReason.IServiceUnavailable);
            return;
        }

        if (!eligibility.Eligible)
        {
            logger.LogWarning(
                "[WORKER] Tenant não elegível (EvaluatedAtUtc={EvaluatedAtUtc}). Cancelando CommandId={CommandId} sem realizar coleta.",
                eligibility.EvaluatedAtUtc,
                command.CommandId);
            await SafeCompleteAsync(command.CommandId, "Cancelled", null);
            return;
        }

        // 3. Executa a coleta
        logger.LogInformation(
            "[WORKER] Executando coleta para CommandId={CommandId} IntegrationId={IntegrationId} Username={Username}.",
            command.CommandId,
            command.IntegrationId,
            command.Credential.Username);

        try
        {
            var result = await iServiceCollector.CollectAsync(
                command.TenantId,
                command.CommandId,
                command.Credential.Username,
                command.Credential.Password,
                command.Credential.BaseUrl,
                command.HistoryWindowMonths,
                cancellationToken);

            // 4. Registra o resumo da coleta — persistência agora é feita de forma
            // incremental por IServiceCollectorService em provider_interactions
            // (Fase 1/Fase 5 do refactor de persistência; work_order_snapshots
            // e IWorkOrderRepository foram removidos no cutover).
            logger.LogInformation(
                "[WORKER] Coleta concluída. TenantId={TenantId} CommandId={CommandId} CapturedAt={CapturedAt} Counts={@StatusCounts}",
                command.TenantId,
                command.CommandId,
                result.CapturedAtUtc,
                result.StatusCounts);

            // 5. Registra sucesso
            await apiClient.CompleteAsync(
                command.CommandId,
                "Succeeded",
                failureReason: null,
                completedAtUtc: DateTimeOffset.UtcNow,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelamento durante a coleta — registra como Cancelled e repropaga para sair do loop
            logger.LogWarning("[WORKER] Coleta cancelada para CommandId={CommandId}.", command.CommandId);
            await SafeCompleteAsync(command.CommandId, "Cancelled", null);
            throw;
        }
        catch (CredentialRejectedEx ex)
        {
            // ATENÇÃO: CredentialRejected desativa o Agente no lado da API.
            // Usar SOMENTE quando o login CAS rejeitar por credencial inválida.
            logger.LogError(ex, "[WORKER] Credencial rejeitada para CommandId={CommandId} Username={Username}.",
                command.CommandId,
                command.Credential.Username);

            await SafeCompleteAsync(command.CommandId, "Failed", ECommandFailureReason.CredentialRejected);
        }
        catch (IServiceUnavailableEx ex)
        {
            logger.LogError(ex, "[WORKER] iService indisponível para CommandId={CommandId}.", command.CommandId);
            await SafeCompleteAsync(command.CommandId, "Failed", ECommandFailureReason.IServiceUnavailable);
        }
        catch (TimeoutException ex)
        {
            logger.LogError(ex, "[WORKER] Timeout durante coleta para CommandId={CommandId}.", command.CommandId);
            await SafeCompleteAsync(command.CommandId, "Failed", ECommandFailureReason.IServiceUnavailable);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[WORKER] Falha de rede durante coleta para CommandId={CommandId}.", command.CommandId);
            await SafeCompleteAsync(command.CommandId, "Failed", ECommandFailureReason.IServiceUnavailable);
        }
        catch (MongoDB.Driver.MongoException ex)
        {
            logger.LogError(ex, "[WORKER] Falha ao persistir OS no MongoDB para CommandId={CommandId}.", command.CommandId);
            await SafeCompleteAsync(command.CommandId, "Failed", ECommandFailureReason.UnexpectedError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[WORKER] Erro inesperado durante coleta para CommandId={CommandId}.", command.CommandId);
            await SafeCompleteAsync(command.CommandId, "Failed", ECommandFailureReason.UnexpectedError);
        }
    }

    /// <summary>
    /// Chama CompleteAsync absorvendo exceções para não mascarar o erro original.
    /// </summary>
    private async Task SafeCompleteAsync(Guid commandId, string outcome, ECommandFailureReason? failureReason)
    {
        try
        {
            await apiClient.CompleteAsync(commandId, outcome, failureReason, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[WORKER] Falha ao registrar Complete para CommandId={CommandId} Outcome={Outcome}.",
                commandId, outcome);
        }
    }
}
