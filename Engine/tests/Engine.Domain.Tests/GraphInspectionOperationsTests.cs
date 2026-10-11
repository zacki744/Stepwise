using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;
using static Engine.Domain.Tests.GraphTestData;

namespace Engine.Domain.Tests;

public class GraphInspectionOperationsTests
{
    //   0 -> 1 -> 3
    //   0 -> 2 -> 3
    private static Graph Diamond() => Directed(4, (0, 1), (0, 2), (1, 3), (2, 3));

    // ---------- traverse ----------

    [Fact]
    public void Traverse_Bfs_VisitsLevelByLevel()
    {
        var result = Assert.IsType<TraversalResult>(new TraverseGraphOperation(0, "bfs").Execute(Diamond()).Value);
        Assert.Equal(new[] { 0, 1, 2, 3 }, result.Nodes);
    }

    [Fact]
    public void Traverse_Dfs_GoesDeepFirst()
    {
        var result = Assert.IsType<TraversalResult>(new TraverseGraphOperation(0, "dfs").Execute(Diamond()).Value);
        Assert.Equal(new[] { 0, 1, 3, 2 }, result.Nodes);
    }

    [Fact]
    public void Traverse_FollowsEdgesAsStored_SoUnreachableNodesAreSkipped()
    {
        var result = Assert.IsType<TraversalResult>(
            new TraverseGraphOperation(0).Execute(Directed(3, (0, 1))).Value);

        Assert.Equal(new[] { 0, 1 }, result.Nodes);
        Assert.Equal(2, result.TotalVisited);
    }

    [Theory]
    [InlineData("bfs")]
    [InlineData("dfs")]
    public void Traverse_AGraphWithCycles_VisitsEachNodeOnce(string order)
    {
        var triangle = Undirected(3, (0, 1), (1, 2), (0, 2));

        var result = Assert.IsType<TraversalResult>(new TraverseGraphOperation(0, order).Execute(triangle).Value);

        Assert.Equal(3, result.TotalVisited);
        Assert.Equal(3, result.Nodes.Distinct().Count());
    }

    [Fact]
    public void Traverse_TraceCarriesPositionAndDepth()
    {
        var result = new TraverseGraphOperation(0, "bfs").Execute(Diamond());

        var last = Assert.IsType<TraversalStepPayload>(result.Trace[^1].Payload);
        Assert.Equal(new TraversalStepPayload(Node: 3, Parent: 1, Position: 4, Depth: 2), last);
    }

    [Theory]
    [InlineData("bfs")]
    [InlineData("dfs")]
    public void Traverse_VeryLongChain_DoesNotOverflowTheStack_AndCapsTheReturnedList(string order)
    {
        var result = Assert.IsType<TraversalResult>(
            new TraverseGraphOperation(0, order).Execute(DirectedChain(200_000)).Value);

        Assert.Equal(200_000, result.TotalVisited);
        Assert.Equal(DatasetLimits.MaxReturnedItems, result.Nodes.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Traverse_UnknownOrder_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new TraverseGraphOperation(0, "sideways"));
    }

    [Fact]
    public void Traverse_StartOutOfRange_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new TraverseGraphOperation(9).Execute(Diamond()));
    }

    // ---------- degree-stats ----------

    [Fact]
    public void DegreeStats_ComputesInAndOutDegrees()
    {
        // 0 -> 1, 0 -> 2, 1 -> 2, 2 -> 2 (self-loop); node 3 has no edges at all
        var graph = Directed(4, (0, 1), (0, 2), (1, 2), (2, 2));

        var stats = Assert.IsType<DegreeStatsResult>(new DegreeStatsOperation().Execute(graph).Value);

        Assert.Equal(new DegreeStatsResult(
            NodeCount: 4, EdgeCount: 4, IsolatedNodes: 1, SelfLoops: 1,
            MinOutDegree: 0, MaxOutDegree: 2, MaxOutDegreeNode: 0,
            MaxInDegree: 3, MaxInDegreeNode: 2, AverageOutDegree: 1.0), stats);
    }

    [Fact]
    public void DegreeStats_EmptyGraph_IsAllZeros()
    {
        var stats = Assert.IsType<DegreeStatsResult>(new DegreeStatsOperation().Execute(Graph.Empty(0)).Value);

        Assert.Equal(new DegreeStatsResult(0, 0, 0, 0, 0, 0, null, 0, null, 0), stats);
    }

    // ---------- has-cycle ----------

    private static CycleResult Cycle(Graph graph, bool directed) =>
        Assert.IsType<CycleResult>(new HasCycleOperation(directed).Execute(graph).Value);

    [Fact]
    public void HasCycle_Undirected_TriangleHasOne()
    {
        var result = Cycle(Undirected(3, (0, 1), (1, 2), (0, 2)), directed: false);
        Assert.Equal(new CycleResult(true, 1, 2), result);
    }

    [Fact]
    public void HasCycle_Undirected_AForestHasNone()
    {
        Assert.False(Cycle(Undirected(4, (0, 1), (1, 2)), directed: false).HasCycle);
    }

    [Fact]
    public void HasCycle_Undirected_ADoubledConnectionIsACycle()
    {
        var doubled = Undirected(2, (0, 1), (0, 1));
        Assert.Equal(new CycleResult(true, 0, 1), Cycle(doubled, directed: false));
    }

    [Fact]
    public void HasCycle_Undirected_ASelfLoopIsACycle()
    {
        Assert.Equal(new CycleResult(true, 1, 1), Cycle(Directed(2, (1, 1)), directed: false));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void HasCycle_Undirected_AConnectionStoredInOneDirectionOnlyIsNotACycle(int from, int to)
    {
        Assert.False(Cycle(Directed(2, (from, to)), directed: false).HasCycle);
    }

    [Fact]
    public void HasCycle_Directed_FindsTheClosingEdge()
    {
        var ring = Directed(3, (0, 1), (1, 2), (2, 0));
        Assert.Equal(new CycleResult(true, 2, 0), Cycle(ring, directed: true));
    }

    [Fact]
    public void HasCycle_Directed_ADagHasNone()
    {
        Assert.False(Cycle(Directed(3, (0, 1), (1, 2), (0, 2)), directed: true).HasCycle);
    }

    [Fact]
    public void HasCycle_Directed_ASelfLoopIsACycle()
    {
        Assert.Equal(new CycleResult(true, 1, 1), Cycle(Directed(2, (1, 1)), directed: true));
    }

    [Fact]
    public void HasCycle_Directed_ASymmetricPairCountsAsATwoCycle()
    {
        // The documented difference between the modes: here 0 <-> 1 is two edges, so it loops.
        var pair = Undirected(2, (0, 1));
        Assert.True(Cycle(pair, directed: true).HasCycle);
        Assert.False(Cycle(pair, directed: false).HasCycle);
    }

    [Fact]
    public void HasCycle_AMazeIsATree_ButOneExtraConnectionMakesACycle()
    {
        var maze = (Graph)new GenerateMazeOperation(10, 10, seed: 1).Execute(Graph.Empty(1)).NewState!;
        Assert.False(Cycle(maze, directed: false).HasCycle);

        // node 0 is (0,0) and node 11 is (1,1): diagonal, so not adjacent in the grid and not already connected
        var braided = (Graph)new AddConnectionOperation(0, 11).Execute(maze).NewState!;
        Assert.True(Cycle(braided, directed: false).HasCycle);
    }

    [Fact]
    public void HasCycle_LargeMaze_RunsAtScale()
    {
        var maze = (Graph)new GenerateMazeOperation(300, 300, seed: 1).Execute(Graph.Empty(1)).NewState!;
        Assert.False(Cycle(maze, directed: false).HasCycle);
    }

    [Fact]
    public void HasCycle_Directed_VeryLongChain_DoesNotOverflowTheStack()
    {
        Assert.False(Cycle(DirectedChain(200_000), directed: true).HasCycle);

        var closed = new Graph(200_000, DirectedChain(200_000).AllDirectedEdges().Append((199_999, 0, 1.0)));
        Assert.True(Cycle(closed, directed: true).HasCycle);
    }
}