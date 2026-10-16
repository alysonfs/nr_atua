using Atua.Channel;
using Atua.Channel.Api;
using Atua.Channel.Configuration;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ChannelWorkerOptions>(
    builder.Configuration.GetSection(ChannelWorkerOptions.SectionName));

builder.Services
    .AddHttpClient<IChannelApiClient, ChannelApiClient>((serviceProvider, client) =>
    {
        var opts = serviceProvider.GetRequiredService<IOptions<ChannelWorkerOptions>>().Value;
        client.BaseAddress = new Uri(opts.ApiBaseUrl);
    });

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Validação de configuração na inicialização (ADR-042: polling em intervalo
// de até 30 segundos) — falha rápido em vez de rodar silenciosamente fora da
// política acordada.
var options = host.Services.GetRequiredService<IOptions<ChannelWorkerOptions>>().Value;
if (options.PollingIntervalSeconds is <= 0 or > 30)
{
    throw new InvalidOperationException(
        $"ChannelWorker:PollingIntervalSeconds deve ser maior que 0 e no máximo 30 (ADR-042). " +
        $"Valor configurado: {options.PollingIntervalSeconds}.");
}

if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
{
    throw new InvalidOperationException("ChannelWorker:ApiBaseUrl é obrigatório.");
}

if (string.IsNullOrWhiteSpace(options.ClaimServiceToken) || string.IsNullOrWhiteSpace(options.CompleteServiceToken))
{
    throw new InvalidOperationException(
        "ChannelWorker:ClaimServiceToken e ChannelWorker:CompleteServiceToken são obrigatórios — configure via " +
        "variável de ambiente ou secrets, nunca hardcoded.");
}

host.Run();
