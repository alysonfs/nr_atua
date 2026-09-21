using Atua.Collector.Consumer;
using Atua.Collector.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;

namespace Atua.Collector.Tests;

/// <summary>
/// Testes de <see cref="ProviderInteractionConsumerWorker.ProcessDocumentAsync"/> (ADR-030):
/// garante que a exclusão inline de <c>provider_interactions</c> é chamada nos cinco
/// caminhos de sucesso do consumer, e nunca antes do commit da transação Postgres
/// correspondente (simulada aqui por <see cref="IWorkOrderPgRepository"/>).
/// </summary>
public class ProviderInteractionConsumerWorkerTests
{
    private readonly IMongoDatabase _mongoDatabase = Substitute.For<IMongoDatabase>();
    private readonly IWorkOrderPgRepository _pgRepository = Substitute.For<IWorkOrderPgRepository>();
    private readonly IProviderInteractionRepository _providerInteractionRepository =
        Substitute.For<IProviderInteractionRepository>();
    private readonly IProviderInteractionOrderAdapterFake _adapter = new();

    private ProviderInteractionConsumerWorker CreateWorker(
        IReadOnlyDictionary<string, IProviderInteractionOrderAdapter>? adapters = null)
    {
        return new ProviderInteractionConsumerWorker(
            _mongoDatabase,
            _pgRepository,
            _providerInteractionRepository,
            adapters ?? new Dictionary<string, IProviderInteractionOrderAdapter>(StringComparer.OrdinalIgnoreCase)
            {
                ["iservice"] = _adapter,
            },
            NullLogger<ProviderInteractionConsumerWorker>.Instance);
    }

    private static BsonDocument BuildDocument(
        Guid id,
        Guid tenantId,
        Guid commandId,
        string interactionType,
        bool success = true,
        string? providerType = "iservice",
        BsonArray? orders = null)
    {
        var doc = new BsonDocument
        {
            ["_id"] = id.ToString(),
            ["tenant_id"] = tenantId.ToString(),
            ["command_id"] = commandId.ToString(),
            ["interaction_type"] = interactionType,
            ["success"] = success,
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["orders"] = orders ?? [],
        };

        if (providerType is not null) doc["provider_type"] = providerType;

        return doc;
    }

    [Fact]
    public async Task Login_AvancaTokenERemoveInteracao()
    {
        var worker = CreateWorker();
        var id = Guid.CreateVersion7();
        var doc = BuildDocument(id, Guid.CreateVersion7(), Guid.CreateVersion7(), "login");

        _pgRepository.AdvanceResumeTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await worker.ProcessDocumentAsync(doc, "{}", CancellationToken.None);

        await _pgRepository.Received(1).AdvanceResumeTokenAsync(
            "{}", "provider-interaction-to-work-order", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _providerInteractionRepository.Received(1).DeleteProcessedAsync(id, Arg.Any<CancellationToken>());
        await _pgRepository.DidNotReceive().ProcessInteractionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ProviderWorkOrderData>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessFalse_AvancaTokenERemoveInteracao()
    {
        var worker = CreateWorker();
        var id = Guid.CreateVersion7();
        var doc = BuildDocument(id, Guid.CreateVersion7(), Guid.CreateVersion7(), "list_query", success: false);

        _pgRepository.AdvanceResumeTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await worker.ProcessDocumentAsync(doc, "{}", CancellationToken.None);

        await _pgRepository.Received(1).AdvanceResumeTokenAsync(
            "{}", "provider-interaction-to-work-order", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _providerInteractionRepository.Received(1).DeleteProcessedAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdapterNaoEncontrado_AvancaTokenERemoveInteracao()
    {
        var worker = CreateWorker();
        var id = Guid.CreateVersion7();
        var doc = BuildDocument(id, Guid.CreateVersion7(), Guid.CreateVersion7(), "list_query", providerType: "provedor-desconhecido");

        _pgRepository.AdvanceResumeTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await worker.ProcessDocumentAsync(doc, "{}", CancellationToken.None);

        await _pgRepository.Received(1).AdvanceResumeTokenAsync(
            "{}", "provider-interaction-to-work-order", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _providerInteractionRepository.Received(1).DeleteProcessedAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrdersVazio_AvancaTokenERemoveInteracao()
    {
        _adapter.OrdersToReturn = [];
        var worker = CreateWorker();
        var id = Guid.CreateVersion7();
        var doc = BuildDocument(id, Guid.CreateVersion7(), Guid.CreateVersion7(), "list_query");

        _pgRepository.AdvanceResumeTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await worker.ProcessDocumentAsync(doc, "{}", CancellationToken.None);

        await _pgRepository.Received(1).AdvanceResumeTokenAsync(
            "{}", "provider-interaction-to-work-order", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _providerInteractionRepository.Received(1).DeleteProcessedAsync(id, Arg.Any<CancellationToken>());
    }

    private static ProviderWorkOrderData CreateOrder() => new(
        WorkOrderProviderId: "OS-1",
        Status: "Aberta",
        WorkOrderProviderNo: null,
        ServiceRequestId: null,
        Amount: null,
        ProviderCreatedAt: null,
        ProviderUpdatedAt: null,
        CustomerType: null,
        CustomerName: null,
        CustomerCpf: null,
        ContactEmail: null,
        ContactPhone: null,
        ContactName: null,
        Address: null,
        ZipCode: null,
        CountryName: null,
        StateName: null,
        CityName: null,
        ProductBrand: null,
        PdCode: null,
        CategoryId: null,
        ProductCategoryCode: null,
        ProductCode: null,
        ProductModel: null,
        ProductStatus: null,
        Symptom: null);

    [Fact]
    public async Task CaminhoCompleto_ProcessaERemoveInteracaoDepoisDoCommit()
    {
        _adapter.OrdersToReturn = [CreateOrder()];

        var worker = CreateWorker();
        var id = Guid.CreateVersion7();
        var doc = BuildDocument(id, Guid.CreateVersion7(), Guid.CreateVersion7(), "list_query");

        _pgRepository.ProcessInteractionAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ProviderWorkOrderData>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await worker.ProcessDocumentAsync(doc, "{}", CancellationToken.None);

        Received.InOrder(() =>
        {
            _pgRepository.ProcessInteractionAsync(
                id, Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ProviderWorkOrderData>>(),
                "{}", "provider-interaction-to-work-order", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
            _providerInteractionRepository.DeleteProcessedAsync(id, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task FalhaNaTransacaoPostgres_NuncaChamaExclusao()
    {
        _adapter.OrdersToReturn = [CreateOrder()];

        var worker = CreateWorker();
        var id = Guid.CreateVersion7();
        var doc = BuildDocument(id, Guid.CreateVersion7(), Guid.CreateVersion7(), "list_query");

        // Simula falha/rollback da transação Postgres (ex.: erro de conexão, violação de
        // constraint) — ProcessInteractionAsync propaga a exceção sem ter dado commit.
        _pgRepository.ProcessInteractionAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ProviderWorkOrderData>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("falha simulada de transação")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.ProcessDocumentAsync(doc, "{}", CancellationToken.None));

        await _providerInteractionRepository.DidNotReceive().DeleteProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Fake simples de adapter — controla os pedidos retornados por teste.</summary>
    private sealed class IProviderInteractionOrderAdapterFake : IProviderInteractionOrderAdapter
    {
        public IReadOnlyList<ProviderWorkOrderData> OrdersToReturn { get; set; } = [];

        public IReadOnlyList<ProviderWorkOrderData> ExtractOrders(BsonArray orders) => OrdersToReturn;
    }
}
