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

    [Fact]
    public void AddConnection_Directed_AddsOnlyTheOneWayEdge()
    {
        var result = new AddConnectionOperation(0, 1, 4.0, directed: true).Execute(Graph.Empty(3));

        var updated = Assert.IsType<Graph>(result.NewState);
        Assert.Contains(1, updated.GetNeighbors(0));
        Assert.DoesNotContain(0, updated.GetNeighbors(1));
        Assert.Equal(1, updated.EdgeCount);
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void AddConnection_Directed_ReverseEdgeDoesNotCountAsExisting()
    {
        var graph = new Graph(2, [(1, 0, 1.0)]);

        var result = new AddConnectionOperation(0, 1, 1.0, directed: true).Execute(graph);

        var updated = Assert.IsType<Graph>(result.NewState);
        Assert.True((bool)result.Value!);
        Assert.Equal(2, updated.EdgeCount); // both directions now exist, as two separate one-way edges
    }

    [Fact]
    public void AddConnection_Directed_AlreadyThere_ReturnsFalse()
    {
        var graph = new Graph(2, [(0, 1, 1.0)]);

        var result = new AddConnectionOperation(0, 1, 1.0, directed: true).Execute(graph);

        Assert.False((bool)result.Value!);
        Assert.Equal(1, ((Graph)result.NewState!).EdgeCount);
    }

    [Fact]
    public void AddConnection_Directed_SelfLoop_AddsASingleEdge()
    {
        var result = new AddConnectionOperation(1, 1, 1.0, directed: true).Execute(Graph.Empty(3));

        Assert.Equal(1, ((Graph)result.NewState!).EdgeCount);
    }

    [Fact]
    public void RemoveConnection_Directed_KeepsTheReverseEdge()
    {
        var graph = new Graph(2, [(0, 1, 1.0), (1, 0, 1.0)]);

        var result = new RemoveConnectionOperation(0, 1, directed: true).Execute(graph);

        var updated = Assert.IsType<Graph>(result.NewState);
        Assert.DoesNotContain(1, updated.GetNeighbors(0));
        Assert.Contains(0, updated.GetNeighbors(1));
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void RemoveConnection_Directed_OnlyReverseExists_ReturnsFalse_AndChangesNothing()
    {
        var graph = new Graph(2, [(1, 0, 1.0)]);

        var result = new RemoveConnectionOperation(0, 1, directed: true).Execute(graph);

        Assert.False((bool)result.Value!);
        Assert.Equal(1, ((Graph)result.NewState!).EdgeCount);
    }

    [Fact]
    public void DirectedAddThenRemove_RoundTripsToTheOriginalGraph()
    {
        var graph = new Graph(3, [(0, 1, 1.0), (1, 0, 1.0)]);

        var added = (Graph)new AddConnectionOperation(1, 2, 2.0, directed: true).Execute(graph).NewState!;
        var removed = (Graph)new RemoveConnectionOperation(1, 2, directed: true).Execute(added).NewState!;

        Assert.Equal(graph.AllDirectedEdges().OrderBy(e => e), removed.AllDirectedEdges().OrderBy(e => e));
    }
}
