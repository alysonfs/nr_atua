using Atua.Collector;
using Atua.Collector.Api;
using Atua.Collector.Configuration;
using Atua.Collector.Consumer;
using Atua.Collector.Diagnostics;
using Atua.Collector.IService;
using Atua.Collector.Persistence;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = Host.CreateApplicationBuilder(args);

// Configuração
builder.Services.Configure<CollectorWorkerOptions>(
    builder.Configuration.GetSection(CollectorWorkerOptions.SectionName));

builder.Services.Configure<MongoDbOptions>(
    builder.Configuration.GetSection(MongoDbOptions.SectionName));

builder.Services.Configure<PostgresOptions>(
    builder.Configuration.GetSection(PostgresOptions.SectionName));

// MongoDB Atlas — IMongoClient singleton, IMongoDatabase scoped por banco (ADR-012)
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MongoDbOptions>>().Value;
    return new MongoClient(opts.ConnectionString);
});

builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MongoDbOptions>>().Value;
    var client = sp.GetRequiredService<IMongoClient>();
    return client.GetDatabase(opts.DatabaseName);
});

builder.Services.AddSingleton<IWorkOrderRepository, WorkOrderRepository>();

// Log de diagnóstico local do ciclo de coleta iService — sempre registrado, mas
// totalmente no-op quando CollectorWorkerOptions.EnableLocalDebugLog = false (padrão
// em produção/AWS). Ver Configuration/CollectorWorkerOptions.cs.
builder.Services.AddSingleton<IIServiceDebugLogger, IServiceDebugLogger>();

// HttpClient tipado para a API Atua
builder.Services
    .AddHttpClient<ICollectorApiClient, CollectorApiClient>((serviceProvider, client) =>
    {
        var opts = serviceProvider
            .GetRequiredService<IOptions<CollectorWorkerOptions>>().Value;
        client.BaseAddress = new Uri(opts.ApiBaseUrl);
    });

// Serviços
builder.Services.AddSingleton<IIServiceCollector, IServiceCollectorService>();
builder.Services.AddHostedService<Worker>();

// Consumer de Change Streams (ADR-023)
builder.Services.AddSingleton<WorkOrderPgRepository>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<WorkOrderPgRepository>>();
    return new WorkOrderPgRepository(opts.ConnectionString, logger);
});

builder.Services.AddSingleton<IReadOnlyDictionary<string, ISnapshotAdapter>>(_ =>
    new Dictionary<string, ISnapshotAdapter>(StringComparer.OrdinalIgnoreCase)
    {
        ["iservice"] = new IServiceSnapshotAdapter()
    });

builder.Services.AddHostedService<SnapshotConsumerWorker>();

var host = builder.Build();

// Garante índices no MongoDB na inicialização (idempotente)
var repo = host.Services.GetRequiredService<IWorkOrderRepository>();
if (repo is WorkOrderRepository concreteRepo)
{
    await concreteRepo.EnsureIndexesAsync();
}

host.Run();

