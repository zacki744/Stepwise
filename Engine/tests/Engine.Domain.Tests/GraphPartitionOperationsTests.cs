using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;
using static Engine.Domain.Tests.GraphTestData;

namespace Engine.Domain.Tests;

public class GraphPartitionOperationsTests
{
    private static BipartiteResult Bipartite(Graph g) =>
        Assert.IsType<BipartiteResult>(new IsBipartiteOperation().Execute(g).Value);
    private static TopologicalSortResult Topo(Graph g) =>
        Assert.IsType<TopologicalSortResult>(new TopologicalSortOperation().Execute(g).Value);
    private static SccResult Scc(Graph g) =>
        Assert.IsType<SccResult>(new StronglyConnectedComponentsOperation().Execute(g).Value);
    private static ColoringResult Coloring(Graph g) =>
        Assert.IsType<ColoringResult>(new GraphColoringOperation().Execute(g).Value);

    private static void AssertProperColoring(Graph graph, IReadOnlyList<int> colors)
    {
        foreach (var (from, to, _) in graph.AllDirectedEdges())
            if (from != to) Assert.NotEqual(colors[from], colors[to]);
    }

    // ---------- is-bipartite ----------

    [Fact]
    public void Bipartite_Path_AlternatesColours()
    {
        var result = Bipartite(Undirected(4, (0, 1), (1, 2), (2, 3)));

        Assert.True(result.IsBipartite);
        Assert.Equal(new[] { 0, 1, 0, 1 }, result.Colors);
    }

    [Fact]
    public void Bipartite_EvenCycleYes_OddCycleNo()
    {
        Assert.True(Bipartite(Undirected(4, (0, 1), (1, 2), (2, 3), (3, 0))).IsBipartite);
        Assert.False(Bipartite(Undirected(5, (0, 1), (1, 2), (2, 3), (3, 4), (4, 0))).IsBipartite);
    }

    [Fact]
    public void Bipartite_Triangle_ReportsTheConflictingEdge()
    {
        var result = Bipartite(Undirected(3, (0, 1), (1, 2), (0, 2)));

        Assert.False(result.IsBipartite);
        Assert.Null(result.Colors);
        Assert.Equal((1, 2), (result.ConflictFrom, result.ConflictTo));
    }

    [Fact]
    public void Bipartite_SelfLoop_IsNeverBipartite()
    {
        var result = Bipartite(Directed(2, (1, 1)));

        Assert.False(result.IsBipartite);
        Assert.Equal((1, 1), (result.ConflictFrom, result.ConflictTo));
    }

    [Fact]
    public void Bipartite_IgnoresDirection_SoOneWayStorageWorks()
    {
        var result = Bipartite(Directed(3, (0, 1), (2, 1))); // both edges point INTO node 1

        Assert.True(result.IsBipartite);
        Assert.Equal(new[] { 0, 1, 0 }, result.Colors);
    }

    [Fact]
    public void Bipartite_DisconnectedAndEmptyGraphs()
    {
        Assert.Equal(new[] { 0, 1, 0, 0 }, Bipartite(Undirected(4, (0, 1))).Colors); // isolated nodes get colour 0
        var empty = Bipartite(Graph.Empty(0));
        Assert.True(empty.IsBipartite);
        Assert.Empty(empty.Colors!);
    }

    [Fact]
    public void Bipartite_LargeTree_Works_AndCapsTheColourList()
    {
        var maze = (Graph)new GenerateMazeOperation(300, 300, seed: 1).Execute(Graph.Empty(1)).NewState!;

        var result = Bipartite(maze);

        Assert.True(result.IsBipartite); // a tree always is
        Assert.Equal(DatasetLimits.MaxReturnedItems, result.Colors!.Count);
        Assert.True(result.ColorsTruncated);
    }

    // ---------- topological-sort ----------

    [Fact]
    public void Topo_TakesTheSmallestAvailableNodeEachTime()
    {
        var result = Topo(Directed(4, (3, 1), (3, 2), (1, 0), (2, 0)));

        Assert.True(result.IsAcyclic);
        Assert.Equal(new[] { 3, 1, 2, 0 }, result.Order);
        Assert.Equal(4, result.SortedCount);
    }

    [Fact]
    public void Topo_NoEdges_IsSimplyAscending()
    {
        Assert.Equal(new[] { 0, 1, 2 }, Topo(Graph.Empty(3)).Order);
    }

    [Fact]
    public void Topo_ACycleBlocksTheSort_ButTheOrderablePrefixIsReported()
    {
        var result = Topo(Directed(3, (0, 1), (1, 2), (2, 1)));

        Assert.False(result.IsAcyclic);
        Assert.Equal(new[] { 0 }, result.Order);
        Assert.Equal(1, result.SortedCount);
    }

    [Fact]
    public void Topo_SelfLoop_CountsAsACycle()
    {
        var result = Topo(Directed(2, (0, 0)));

        Assert.False(result.IsAcyclic);
        Assert.Equal(new[] { 1 }, result.Order);
    }

    [Fact]
    public void Topo_AgreesWithDirectedCycleDetection()
    {
        var ring = Directed(3, (0, 1), (1, 2), (2, 0));
        var dag = Directed(3, (0, 1), (1, 2), (0, 2));

        Assert.Equal(!Topo(ring).IsAcyclic, ((CycleResult)new HasCycleOperation(directed: true).Execute(ring).Value!).HasCycle);
        Assert.Equal(!Topo(dag).IsAcyclic, ((CycleResult)new HasCycleOperation(directed: true).Execute(dag).Value!).HasCycle);
    }

    [Fact]
    public void Topo_VeryLongChain_Works_AndCapsTheOrderList()
    {
        var result = Topo(DirectedChain(200_000));

        Assert.True(result.IsAcyclic);
        Assert.Equal(200_000, result.SortedCount);
        Assert.Equal(DatasetLimits.MaxReturnedItems, result.Order.Count);
        Assert.True(result.Truncated);
    }

    // ---------- strongly-connected-components ----------

    [Fact]
    public void Scc_FindsTheCyclesAndListsSinkComponentsFirst()
    {
        var result = Scc(Directed(5, (0, 1), (1, 2), (2, 0), (2, 3), (3, 4), (4, 3)));

        Assert.Equal(2, result.ComponentCount);
        Assert.Equal(3, result.LargestComponentSize);
        Assert.Equal(new[] { new[] { 3, 4 }, new[] { 0, 1, 2 } }, result.Components);
    }

    [Fact]
    public void Scc_ADagIsAllSingletons()
    {
        Assert.Equal(4, Scc(Directed(4, (0, 1), (1, 2), (0, 3))).ComponentCount);
    }

    [Fact]
    public void Scc_AnUndirectedGraph_HasOneComponentPerConnectedPiece()
    {
        Assert.Equal(2, Scc(Undirected(4, (0, 1), (2, 3))).ComponentCount);
    }

    [Fact]
    public void Scc_AHugeRing_DoesNotOverflowTheStack_AndIsTooBigToList()
    {
        int n = 100_000;
        var ring = new Graph(n, Enumerable.Range(0, n).Select(i => (From: i, To: (i + 1) % n)));

        var result = Scc(ring);

        Assert.Equal(1, result.ComponentCount);
        Assert.Equal(n, result.LargestComponentSize);
        Assert.Empty(result.Components); // bigger than the listing cap, but still counted exactly
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Scc_ALongChain_CountsEverything_AndCapsTheListing()
    {
        var result = Scc(DirectedChain(200_000));

        Assert.Equal(200_000, result.ComponentCount);
        Assert.Equal(DatasetLimits.MaxReturnedItems, result.Components.Count);
        Assert.True(result.Truncated);
    }

    // ---------- graph-coloring ----------

    [Fact]
    public void Coloring_CompleteGraphNeedsOneColourPerNode()
    {
        var k4 = Undirected(4, (0, 1), (0, 2), (0, 3), (1, 2), (1, 3), (2, 3));

        var result = Coloring(k4);

        Assert.Equal(4, result.ColorCount);
        Assert.Equal(new[] { 0, 1, 2, 3 }, result.Colors);
    }

    [Fact]
    public void Coloring_TriangleUsesThree_PathUsesTwo()
    {
        Assert.Equal(3, Coloring(Undirected(3, (0, 1), (1, 2), (0, 2))).ColorCount);

        var path = Coloring(Undirected(4, (0, 1), (1, 2), (2, 3)));
        Assert.Equal(2, path.ColorCount);
        Assert.Equal(new[] { 1, 0, 1, 0 }, path.Colors); // highest-degree nodes are coloured first
    }

    [Fact]
    public void Coloring_EdgelessAndEmptyGraphs()
    {
        var edgeless = Coloring(Graph.Empty(5));
        Assert.Equal(1, edgeless.ColorCount);
        Assert.All(edgeless.Colors, c => Assert.Equal(0, c));

        var empty = Coloring(Graph.Empty(0));
        Assert.True(empty.Colorable);
        Assert.Equal(0, empty.ColorCount);
    }

    [Fact]
    public void Coloring_ASelfLoopCannotBeColoured()
    {
        Assert.False(Coloring(Directed(2, (1, 1))).Colorable);
    }

    [Fact]
    public void Coloring_ARandomGraph_IsProper_AndWithinTheDegreeBound()
    {
        var graph = (Graph)new GenerateRandomGraphOperation(200, 600, seed: 3).Execute(Graph.Empty(1)).NewState!;

        var result = Coloring(graph);

        AssertProperColoring(graph, result.Colors);
        int maxDegree = Enumerable.Range(0, graph.NodeCount).Max(graph.Degree);
        Assert.True(result.ColorCount <= maxDegree + 1);
    }
}