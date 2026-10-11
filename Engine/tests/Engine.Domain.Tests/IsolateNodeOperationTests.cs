using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class IsolateNodeOperationTests
{
    [Fact]
    public void Execute_RemovesAllConnectionsToAndFromNode()
    {
        var graph = new Graph(3, [(0, 1, 1.0), (1, 0, 1.0), (1, 2, 1.0), (2, 1, 1.0)]); // 1 connects to both 0 and 2

        var result = new IsolateNodeOperation(nodeIndex: 1).Execute(graph);

        var updated = Assert.IsType<Graph>(result.NewState);
        Assert.Empty(updated.GetNeighbors(1));
        Assert.Equal(2, result.Value); // two undirected connections severed
    }

    [Fact]
    public void Execute_LeavesUnrelatedEdgesUntouched()
    {
        var graph = new Graph(4, [(0, 1, 1.0), (1, 0, 1.0), (2, 3, 1.0), (3, 2, 1.0)]);

        var result = new IsolateNodeOperation(nodeIndex: 0).Execute(graph);

        var updated = Assert.IsType<Graph>(result.NewState);
        Assert.Contains(3, updated.GetNeighbors(2)); // 2-3 connection untouched
    }

    [Fact]
    public void Execute_NodeIndexRemainsAddressable_JustWithNoNeighbors()
    {
        var graph = new Graph(2, [(0, 1, 1.0), (1, 0, 1.0)]);

        var result = new IsolateNodeOperation(nodeIndex: 0).Execute(graph);

        var updated = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(2, updated.NodeCount); // node count unchanged — index 0 still exists
        Assert.Empty(updated.GetNeighbors(0));
    }

    [Fact]
    public void Execute_AlreadyIsolatedNode_ReturnsZero()
    {
        var result = new IsolateNodeOperation(nodeIndex: 0).Execute(Graph.Empty(3));
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void Execute_OutOfRangeIndex_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IsolateNodeOperation(nodeIndex: 99).Execute(Graph.Empty(3)));
    }

    [Fact]
    public void Execute_DoesNotMutateInputGraph()
    {
        var graph = new Graph(2, [(0, 1, 1.0), (1, 0, 1.0)]);
        new IsolateNodeOperation(nodeIndex: 0).Execute(graph);
        Assert.Contains(1, graph.GetNeighbors(0)); // original untouched
    }
}