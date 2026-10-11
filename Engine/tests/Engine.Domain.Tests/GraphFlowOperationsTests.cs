using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;
using static Engine.Domain.Tests.GraphTestData;

namespace Engine.Domain.Tests;

public class GraphFlowOperationsTests
{
    // The classic 6-node network whose maximum flow from 0 to 5 is 23.
    private static Graph Textbook() => new(6, [
        (0, 1, 16.0), (0, 2, 13.0), (1, 2, 10.0), (2, 1, 4.0), (1, 3, 12.0),
        (3, 2, 9.0), (2, 4, 14.0), (4, 3, 7.0), (3, 5, 20.0), (4, 5, 4.0)]);

    private static MaxFlowResult Flow(Graph g, int source, int sink) =>
        Assert.IsType<MaxFlowResult>(new MaxFlowOperation(source, sink).Execute(g).Value);
    private static MatchingResult Matching(Graph g) =>
        Assert.IsType<MatchingResult>(new BipartiteMatchingOperation().Execute(g).Value);

    private static void AssertValidMatching(Graph graph, MatchingResult result)
    {
        var connections = graph.AllDirectedEdges().Select(e => (Math.Min(e.From, e.To), Math.Max(e.From, e.To))).ToHashSet();
        var used = new HashSet<int>();
        foreach (var pair in result.Pairs)
        {
            Assert.Contains((Math.Min(pair.Left, pair.Right), Math.Max(pair.Left, pair.Right)), connections);
            Assert.True(used.Add(pair.Left), $"node {pair.Left} matched twice");
            Assert.True(used.Add(pair.Right), $"node {pair.Right} matched twice");
        }
        Assert.Equal(result.MatchingSize, result.Pairs.Count);
    }

    // ---------- max-flow ----------

    [Fact]
    public void MaxFlow_TheTextbookNetwork()
    {
        Assert.Equal(23.0, Flow(Textbook(), 0, 5).Value, precision: 9);
    }

    [Fact]
    public void MaxFlow_ObeysConservationAndCapacity()
    {
        var result = Flow(Textbook(), 0, 5);
        var net = new double[6];
        foreach (var edge in result.Flows)
        {
            Assert.InRange(edge.Flow, 0, edge.Capacity + 1e-9);
            net[edge.From] -= edge.Flow;
            net[edge.To] += edge.Flow;
        }

        for (int node = 1; node < 5; node++) Assert.Equal(0.0, net[node], precision: 9);
        Assert.Equal(result.Value, net[5], precision: 9);
        Assert.Equal(-result.Value, net[0], precision: 9);
    }

    [Fact]
    public void MaxFlow_TheMinimumCutHasTheSameValueAsTheFlow()
    {
        var result = Flow(Textbook(), 0, 5);

        Assert.Equal(result.Value, result.MinCut.Sum(edge => edge.Capacity), precision: 9);
        Assert.All(result.MinCut, edge => Assert.Equal(edge.Capacity, edge.Flow, precision: 9)); // every cut edge is saturated
    }

    [Fact]
    public void MaxFlow_ParallelEdgesAdd_AndFractionsAreFine()
    {
        Assert.Equal(7.0, Flow(new Graph(2, [(0, 1, 3.0), (0, 1, 4.0)]), 0, 1).Value, precision: 9);
        Assert.Equal(0.25, Flow(new Graph(3, [(0, 1, 0.5), (1, 2, 0.25)]), 0, 2).Value, precision: 9);
    }

    [Fact]
    public void MaxFlow_NoRoute_IsZero_WithNothingToList()
    {
        var result = Flow(new Graph(3, [(0, 1, 5.0)]), 0, 2);

        Assert.Equal(0.0, result.Value);
        Assert.Empty(result.Flows);
        Assert.Empty(result.MinCut);
    }

    [Fact]
    public void MaxFlow_InvalidInput_IsRejected()
    {
        var graph = new Graph(3, [(0, 1, 1.0)]);
        Assert.Throws<ArgumentException>(() => new MaxFlowOperation(1, 1).Execute(graph));
        Assert.Throws<ArgumentException>(() => new MaxFlowOperation(0, 9).Execute(graph));
        Assert.Throws<ArgumentException>(() =>
            new MaxFlowOperation(0, 1).Execute(new Graph(2, [(0, 1, -1.0)])));
    }

    [Fact]
    public void MaxFlow_IsStoppedByTheWorkBudget()
    {
        var context = new OperationContext(CancellationToken.None, null, maxWorkUnits: 1_000);

        Assert.Throws<OperationAbortedException>(() =>
            new MaxFlowOperation(0, 4_999).Execute(DirectedChain(5_000), context));
    }

    // ---------- bipartite-matching ----------

    [Fact]
    public void Matching_FindsAMaximumMatching()
    {
        var graph = Undirected(4, (0, 2), (0, 3), (1, 2));

        var result = Matching(graph);

        Assert.True(result.IsBipartite);
        Assert.Equal(2, result.MatchingSize);
        AssertValidMatching(graph, result);
    }

    [Fact]
    public void Matching_WhenOneNodeLimitsEveryoneElse_OnlyOneCanBeMatched()
    {
        var graph = Undirected(4, (0, 3), (1, 3), (2, 3)); // three nodes all competing for node 3

        Assert.Equal(1, Matching(graph).MatchingSize);
    }

    [Fact]
    public void Matching_OneWayStorage_GivesTheForcedAnswer()
    {
        var result = Matching(Directed(4, (0, 2), (1, 2), (1, 3))); // 0 can only take 2, so 1 must take 3

        Assert.Equal(new[] { new MatchedPair(0, 2), new MatchedPair(1, 3) }, result.Pairs);
    }

    [Fact]
    public void Matching_ANonBipartiteGraph_IsReportedAsSuch()
    {
        var result = Matching(Undirected(3, (0, 1), (1, 2), (0, 2)));

        Assert.False(result.IsBipartite);
        Assert.Equal(0, result.MatchingSize);
    }

    [Fact]
    public void Matching_NoEdges_IsEmpty()
    {
        var result = Matching(Graph.Empty(4));

        Assert.True(result.IsBipartite);
        Assert.Equal(0, result.MatchingSize);
    }

    [Fact]
    public void Matching_ALongPathMatchesEveryOtherEdge()
    {
        var path = new Graph(1_000, Enumerable.Range(0, 999).SelectMany(i =>
            new[] { (From: i, To: i + 1), (From: i + 1, To: i) }));

        var result = Matching(path);

        Assert.Equal(500, result.MatchingSize);
        AssertValidMatching(path, result);
    }

    [Fact]
    public void Matching_ACompleteBipartiteGraph_MatchesEveryone()
    {
        var edges = Enumerable.Range(0, 50).SelectMany(l => Enumerable.Range(50, 50).Select(r => (l, r))).ToArray();
        var graph = Undirected(100, edges);

        var result = Matching(graph);

        Assert.Equal(50, result.MatchingSize);
        AssertValidMatching(graph, result);
    }

    [Fact]
    public void Matching_IsDeterministic()
    {
        var graph = (Graph)new GenerateRandomGraphOperation(30, 40, seed: 2).Execute(Graph.Empty(1)).NewState!;

        Assert.Equal(Matching(graph), Matching(graph));
    }
}