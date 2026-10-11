using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class ConnectionOperationsTests
{
    [Fact]
    public void AddConnection_NewEdge_AddsBothDirections_ReturnsTrue()
    {
        var result = new AddConnectionOperation(0, 1, 5.0).Execute(Graph.Empty(3));

        var updated = Assert.IsType<Graph>(result.NewState);
        Assert.Contains(1, updated.GetNeighbors(0));
        Assert.Contains(0, updated.GetNeighbors(1));
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void AddConnection_AlreadyExists_ReturnsFalse_NoDuplicate()
    {
        var graph = new Graph(3, [(0, 1, 5.0), (1, 0, 5.0)]);

        var result = new AddConnectionOperation(0, 1, 5.0).Execute(graph);

        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void AddConnection_DoesNotMutateInputGraph()
    {
        var graph = Graph.Empty(2);
        new AddConnectionOperation(0, 1, 1.0).Execute(graph);
        Assert.Empty(graph.GetNeighbors(0));
    }

    [Fact]
    public void RemoveConnection_ExistingEdge_RemovesBothDirections_ReturnsTrue()
    {
        var graph = new Graph(2, [(0, 1, 1.0), (1, 0, 1.0)]);

        var result = new RemoveConnectionOperation(0, 1).Execute(graph);

        var updated = Assert.IsType<Graph>(result.NewState);
        Assert.DoesNotContain(1, updated.GetNeighbors(0));
        Assert.DoesNotContain(0, updated.GetNeighbors(1));
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void RemoveConnection_NonExistentEdge_ReturnsFalse()
    {
        var result = new RemoveConnectionOperation(0, 1).Execute(Graph.Empty(2));
        Assert.False((bool)result.Value!);
    }
}