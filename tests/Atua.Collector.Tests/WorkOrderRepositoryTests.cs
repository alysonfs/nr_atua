using Atua.Collector.Persistence;
using MongoDB.Bson;

namespace Atua.Collector.Tests;

/// <summary>
/// Testes unitários de <see cref="WorkOrderRepository.BuildRawDocument"/> (RF-016.2).
/// Cobre o BUG-016-001: campos com valor <c>null</c> no payload do provedor devem
/// ser preservados no <c>rawdata</c> persistido, não descartados.
/// </summary>
public class WorkOrderRepositoryTests
{
    [Fact]
    public void BuildRawDocument_PreservesNullFields()
    {
        var orderDict = new Dictionary<string, object?>
        {
            ["workOrderId"] = 101538114L,
            ["closedAt"] = null,
            ["assignedTo"] = null,
            ["woStatus"] = "accepted"
        };

        var result = WorkOrderRepository.BuildRawDocument(orderDict);

        Assert.True(result.Contains("closedAt"));
        Assert.True(result.Contains("assignedTo"));
        Assert.Equal(BsonNull.Value, result["closedAt"]);
        Assert.Equal(BsonNull.Value, result["assignedTo"]);
        Assert.Equal(4, result.ElementCount);
    }

    [Fact]
    public void BuildRawDocument_PreservesNonNullValuesUnchanged()
    {
        var orderDict = new Dictionary<string, object?>
        {
            ["workOrderId"] = 101538114L,
            ["woStatus"] = "accepted",
            ["openDays"] = 0.1
        };

        var result = WorkOrderRepository.BuildRawDocument(orderDict);

        Assert.Equal(101538114L, result["workOrderId"].AsInt64);
        Assert.Equal("accepted", result["woStatus"].AsString);
        Assert.Equal(0.1, result["openDays"].AsDouble);
    }

    [Fact]
    public void BuildRawDocument_EmptyDictionary_ReturnsEmptyDocument()
    {
        var orderDict = new Dictionary<string, object?>();

        var result = WorkOrderRepository.BuildRawDocument(orderDict);

        Assert.Equal(0, result.ElementCount);
    }
}
