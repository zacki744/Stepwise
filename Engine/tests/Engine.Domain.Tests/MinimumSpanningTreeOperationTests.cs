using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class MinimumSpanningTreeOperationTests
{
    [Fact]
    public void Execute_ConnectedGraph_ProducesNodeCountMinusOneEdges()
    {
        var graph = new Graph(4, [(0, 1, 1.0), (1, 0, 1.0), (1, 2, 2.0), (2, 1, 2.0),
                                   (2, 3, 3.0), (3, 2, 3.0), (0, 3, 10.0), (3, 0, 10.0)]);

        var result = new MinimumSpanningTreeOperation().Execute(graph);

        var mst = Assert.IsType<Graph>(result.NewState);
        Assert.True((bool)result.Value!);
        int edgeCount = Enumerable.Range(0, 4).Sum(i => mst.GetNeighbors(i).Count()) / 2;
        Assert.Equal(3, edgeCount);
    }

    [Fact]
    public void Execute_PicksMinimumWeightEdges_SkipsExpensiveRedundantOne()
    {
        // 0-3 direct (weight 10) is more expensive than going 0-1-2-3 (1+2+3=6)
        var graph = new Graph(4, [(0, 1, 1.0), (1, 0, 1.0), (1, 2, 2.0), (2, 1, 2.0),
                                   (2, 3, 3.0), (3, 2, 3.0), (0, 3, 10.0), (3, 0, 10.0)]);

        var result = new MinimumSpanningTreeOperation().Execute(graph);
        var mst = Assert.IsType<Graph>(result.NewState);

        Assert.DoesNotContain(3, mst.GetNeighbors(0)); // the expensive 0-3 edge was correctly rejected
    }

    [Fact]
    public void Execute_DisconnectedGraph_ProducesForest_ValueIsFalse()
    {
        var graph = new Graph(4, [(0, 1, 1.0), (1, 0, 1.0)]); // 2 and 3 isolated

        var result = new MinimumSpanningTreeOperation().Execute(graph);

        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void Execute_SameInput_ProducesIdenticalResult()
    {
        var graph = new Graph(4, [(0, 1, 1.0), (1, 0, 1.0), (1, 2, 2.0), (2, 1, 2.0)]);

        var a = new MinimumSpanningTreeOperation().Execute(graph);
        var b = new MinimumSpanningTreeOperation().Execute(graph);

        Assert.Equal(
            ((Graph)a.NewState!).AllDirectedEdges().ToHashSet(),
            ((Graph)b.NewState!).AllDirectedEdges().ToHashSet());
    }
}