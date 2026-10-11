using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class GenerateRandomGraphOperationTests
{
    private static Graph Generate(int nodes, int connections, int seed, bool directed = false, int maxWeight = 1) =>
        Assert.IsType<Graph>(new GenerateRandomGraphOperation(nodes, connections, seed, directed, maxWeight)
            .Execute(Graph.Empty(1)).NewState);

    [Fact]
    public void Undirected_HasExactlyTheRequestedDistinctConnections_StoredBothWays()
    {
        var graph = Generate(10, 15, seed: 1);

        Assert.Equal(30, graph.EdgeCount);
        var pairs = graph.AllDirectedEdges().Select(e => (e.From, e.To)).ToHashSet();
        Assert.Equal(30, pairs.Count);                                // no duplicates
        Assert.All(pairs, p => Assert.NotEqual(p.From, p.To));         // no self-loops
        Assert.All(pairs, p => Assert.Contains((p.To, p.From), pairs)); // symmetric
    }

    [Fact]
    public void Directed_HasExactlyTheRequestedDistinctEdges()
    {
        var graph = Generate(10, 40, seed: 1, directed: true);

        var pairs = graph.AllDirectedEdges().Select(e => (e.From, e.To)).ToHashSet();
        Assert.Equal(40, graph.EdgeCount);
        Assert.Equal(40, pairs.Count);
        Assert.All(pairs, p => Assert.NotEqual(p.From, p.To));
    }

    [Fact]
    public void AskingForEveryPossibleConnection_GivesTheCompleteGraph()
    {
        var graph = Generate(6, 15, seed: 3);

        Assert.All(Enumerable.Range(0, 6), node => Assert.Equal(5, graph.Degree(node)));
    }

    [Fact]
    public void SameSeedSameGraph_DifferentSeedDifferentGraph()
    {
        Assert.Equal(GraphTestData.Edges(Generate(20, 30, seed: 7)), GraphTestData.Edges(Generate(20, 30, seed: 7)));
        Assert.NotEqual(GraphTestData.Edges(Generate(20, 30, seed: 7)), GraphTestData.Edges(Generate(20, 30, seed: 8)));
    }

    [Fact]
    public void Weights_AreIntegersInRange_AndEqualInBothDirections()
    {
        var graph = Generate(15, 30, seed: 2, maxWeight: 5);

        foreach (var (from, to, weight) in graph.AllDirectedEdges())
        {
            Assert.InRange(weight, 1.0, 5.0);
            Assert.Equal(Math.Floor(weight), weight);
            Assert.Equal(weight, graph.GetWeight(to, from));
        }
    }

    [Fact]
    public void ZeroConnections_GivesAnEdgelessGraph()
    {
        var graph = Generate(5, 0, seed: 1);

        Assert.Equal(5, graph.NodeCount);
        Assert.Equal(0, graph.EdgeCount);
    }

    [Fact]
    public void ALargeSparseGraph_IsGeneratedQuickly()
    {
        Assert.Equal(400_000, Generate(100_000, 200_000, seed: 1).EdgeCount);
    }

    [Fact]
    public void ANearlyCompleteDenseGraph_DoesNotSlowDown()
    {
        // 400,000 of the 499,500 possible pairs: naive pick-and-retry sampling struggles here, Floyd's does not
        Assert.Equal(800_000, Generate(1_000, 400_000, seed: 1).EdgeCount);
    }

    [Theory]
    [InlineData(-1, 0, false)]
    [InlineData(5, -1, false)]
    [InlineData(5, 11, false)]            // only 10 undirected pairs exist among 5 nodes
    [InlineData(3, 7, true)]              // only 6 ordered pairs among 3 nodes
    [InlineData(5_000, 1_250_001, false)] // would be 2,500,002 directed edges, over the limit
    [InlineData(DatasetLimits.MaxCells + 1, 0, false)]
    public void InvalidSizes_AreRejected(int nodes, int connections, bool directed)
    {
        Assert.Throws<ArgumentException>(() => new GenerateRandomGraphOperation(nodes, connections, seed: 1, directed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GenerateRandomGraphOperation.MaxWeightLimit + 1)]
    public void InvalidMaxWeight_IsRejected(int maxWeight)
    {
        Assert.Throws<ArgumentException>(() => new GenerateRandomGraphOperation(5, 3, seed: 1, maxWeight: maxWeight));
    }
}