using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class ReliabilityRegressionTests
{
    private static Graph PathGraph(int n)
    {
        var edges = new List<(int, int, double)>();
        for (int i = 0; i < n - 1; i++) { edges.Add((i, i + 1, 1.0)); edges.Add((i + 1, i, 1.0)); }
        return new Graph(n, edges);
    }

    // The recursive versions overflowed the call stack on inputs like these and killed the process.
    [Fact]
    public void FindBridges_VeryDeepChain_DoesNotOverflowTheStack()
    {
        var result = new FindBridgesOperation().Execute(PathGraph(200_000));
        Assert.Equal(199_999, ((List<BridgeEdge>)result.Value!).Count);
    }

    [Fact]
    public void FindArticulationPoints_VeryDeepChain_DoesNotOverflowTheStack()
    {
        var result = new FindArticulationPointsOperation().Execute(PathGraph(200_000));
        Assert.Equal(199_998, ((List<int>)result.Value!).Count); // every node except the two ends
    }

    [Fact]
    public void FindBridges_LargeGeneratedMaze_EveryPassageIsABridge()
    {
        var maze = (Graph)new GenerateMazeOperation(300, 300, seed: 1).Execute(Graph.Empty(1)).NewState!;
        var result = new FindBridgesOperation().Execute(maze);
        Assert.Equal(89_999, ((List<BridgeEdge>)result.Value!).Count); // a spanning tree has no spare edges
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void ReachabilityQuery_NodeOutOfRange_IsRejectedAsBadInput(int startNode)
    {
        Assert.Throws<ArgumentException>(() => new ReachabilityQuery(startNode).Execute(Graph.Empty(3)));
    }

    [Fact]
    public void ShortestPath_TargetOutOfRange_IsRejectedBeforeAnyWork()
    {
        var graph = new Graph(3, [(0, 1, 1.0)]);
        Assert.Throws<ArgumentException>(() => new ShortestPathOperation(0, 7).Execute(graph));
    }

    [Fact]
    public void ShortestPath_NegativeWeight_IsRejectedInsteadOfReturningAWrongPath()
    {
        var graph = new Graph(3, [(0, 1, 5.0), (1, 2, -2.0)]);
        Assert.Throws<ArgumentException>(() => new ShortestPathOperation(0, 2).Execute(graph));
    }

    [Fact]
    public void PlaceItems_ItemIdZero_IsRejected()
    {
        var op = new PlaceItemsOperation([new BinItem(Id: 0, Width: 1, Height: 1)]);
        Assert.Throws<ArgumentException>(() => op.Execute(new Matrix(2, 2)));
    }

    [Fact]
    public void PlaceItems_ZeroSizedItem_IsRejected()
    {
        var op = new PlaceItemsOperation([new BinItem(Id: 1, Width: 0, Height: 3)]);
        Assert.Throws<ArgumentException>(() => op.Execute(new Matrix(2, 2)));
    }

    [Fact]
    public void PlaceItems_DuplicateIds_AreRejected()
    {
        var op = new PlaceItemsOperation([new BinItem(1, 1, 1), new BinItem(1, 1, 1)]);
        Assert.Throws<ArgumentException>(() => op.Execute(new Matrix(2, 2)));
    }

    [Fact]
    public void PlaceItems_TooManyItems_IsRejected()
    {
        var items = Enumerable.Range(1, PlaceItemsOperation.MaxItems + 1)
            .Select(i => new BinItem(i, 1, 1)).ToList();
        Assert.Throws<ArgumentException>(() => new PlaceItemsOperation(items).Execute(new Matrix(100, 100)));
    }

    [Fact]
    public void Insert_WhenTreeIsAtSlotLimit_Throws()
    {
        int n = DatasetLimits.MaxCells;
        var full = Tree.FromArrays(
            new List<int?>(Enumerable.Repeat<int?>(0, n)),
            Enumerable.Repeat(-1, n).ToList(),
            Enumerable.Repeat(-1, n).ToList(),
            root: 0);

        Assert.Throws<ArgumentException>(() => new InsertNodeOperation(5).Execute(full));
    }
}