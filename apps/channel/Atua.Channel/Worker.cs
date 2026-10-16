using Atua.Channel.Api;
using Atua.Channel.Configuration;
using Microsoft.Extensions.Options;

namespace Atua.Channel;

/// <summary>
/// Worker do processo do canal WhatsApp (ADR-042).
///
/// Este incremento ainda não integra o Baileys: o envio real do template é
/// um incremento futuro. Aqui o worker prova o transporte de ponta a ponta —
/// reivindica o comando elegível via <c>claim</c> e reporta sempre
/// <c>Success</c> via <c>complete</c> (sucesso fake), sem nunca chamar um
/// canal síncrono em processo (substituindo definitivamente
/// <c>ITriagemChannel</c>/<c>FakeTriagemChannel</c> do incremento anterior,
/// conforme o ADR-042).
///
/// Loop: claim → (se houver comando) complete(Success) → tenta um novo
/// claim imediatamente (throughput: o intervalo de polling nunca é pago
/// depois de um comando concluído, apenas quando não há trabalho). Em 204
/// (sem comando elegível) ou lease perdida (409 — o token ClaimedAtUtc
/// apresentado não é mais o vigente), aguarda
/// <see cref="ChannelWorkerOptions.PollingIntervalSeconds"/> antes de tentar
/// novamente. Nunca expõe porta de entrada — apenas conexões de saída para a
/// Master API (arquitetura, seção 2.4).
/// </summary>
public sealed class Worker(
    IChannelApiClient apiClient,
    IOptions<ChannelWorkerOptions> options,
    ILogger<Worker> logger)
    : BackgroundService
{
    private readonly ChannelWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "[WORKER] Processo do canal iniciado. PollingIntervalSeconds={PollingIntervalSeconds}.",
            _options.PollingIntervalSeconds);

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
                // Exceção inesperada no ciclo — loga e aguarda antes de tentar
                // novamente para evitar loop frenético contra a Master API.
                logger.LogError(ex,
                    "[WORKER] Exceção não tratada no ciclo principal. Aguardando antes de reiniciar.");
                await DelayAsync(stoppingToken);
            }
        }

        logger.LogInformation("[WORKER] Processo do canal encerrado.");
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var command = await apiClient.ClaimAsync(cancellationToken);

        if (command is null)
        {
            logger.LogDebug("[WORKER] Nenhum comando de canal disponível. Aguardando {Seconds}s.",
                _options.PollingIntervalSeconds);
            await DelayAsync(cancellationToken);
            return;
        }

        // Sucesso fake (ADR-042): o envio real via Baileys é um incremento
        // futuro — este processo já prova claim/lease/complete de ponta a
        // ponta contra a Master API real.
        var completed = await apiClient.CompleteAsync(command.CommandId, command.ClaimedAtUtc, "Success",
            errorCode: null, errorDescription: null, cancellationToken);

        if (completed is null)
        {
            // Lease perdida (409): outra instância já reivindicou este
            // comando depois da lease expirar. Nada a aguardar por causa
            // deste comando — tenta um novo claim imediatamente, igual ao
            // caminho de sucesso.
            logger.LogWarning(
                "[WORKER] Lease perdida ao concluir CommandId={CommandId} — tentando novo claim imediatamente.",
                command.CommandId);
        }

        // Throughput (ADR-042): após concluir (ou perder a lease de) um
        // comando, tenta reivindicar o próximo imediatamente — só aguarda o
        // intervalo de polling quando não há trabalho (204, acima) ou em
        // caso de erro (catch de ExecuteAsync).
    }

    private Task DelayAsync(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), cancellationToken);
}
