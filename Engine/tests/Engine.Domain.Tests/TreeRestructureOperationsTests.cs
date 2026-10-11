using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Domain.Tracing;
using Xunit;

namespace Engine.Domain.Tests;

public class TreeRestructureOperationsTests
{
    private static List<int> Values(Tree tree) => tree.InOrder().Select(tree.GetValue).ToList();

    // ---------- tree-clear ----------

    [Fact]
    public void Clear_EmptiesTheTree_AndReportsHowManyNodesWereRemoved()
    {
        var result = new ClearTreeOperation().Execute(TreeTestData.Build(5, 3, 8));

        var cleared = Assert.IsType<Tree>(result.NewState);
        Assert.Null(cleared.RootIndex);
        Assert.Equal(0, cleared.SlotCount);
        Assert.Equal(3, result.Value);
    }

    [Fact]
    public void Clear_ReportsLiveNodes_NotAllocatedSlots()
    {
        var afterRemove = (Tree)new RemoveNodeOperation(3).Execute(TreeTestData.Build(5, 3, 8)).NewState!;

        Assert.Equal(2, new ClearTreeOperation().Execute(afterRemove).Value);
    }

    // ---------- tree-rebuild-balanced ----------

    [Fact]
    public void Rebuild_DegenerateChain_BecomesMinimumHeight_WithTheSameInOrder()
    {
        var chain = TreeTestData.Build(Enumerable.Range(1, 15).ToArray()); // sorted inserts: a 15-deep chain

        var result = new RebuildBalancedTreeOperation().Execute(chain);

        var rebuilt = Assert.IsType<Tree>(result.NewState);
        var summary = Assert.IsType<TreeRebuildResult>(result.Value);
        Assert.Equal(15, summary.HeightBefore);
        Assert.Equal(4, summary.HeightAfter); // a perfect tree of 15 nodes
        Assert.Equal(Enumerable.Range(1, 15), Values(rebuilt));
    }

    [Fact]
    public void Rebuild_KeepsDuplicatesToTheRight_MatchingInsert()
    {
        var rebuilt = Assert.IsType<Tree>(new RebuildBalancedTreeOperation().Execute(TreeTestData.Build(5, 5, 5)).NewState);

        Assert.Null(rebuilt.LeftChild(rebuilt.RootIndex!.Value));
        Assert.Equal(new[] { 5, 5, 5 }, Values(rebuilt));
    }

    [Fact]
    public void Rebuild_ReclaimsSlotsLeftBehindByRemovals()
    {
        var afterRemove = (Tree)new RemoveNodeOperation(3).Execute(TreeTestData.Build(5, 3, 8)).NewState!;
        Assert.Equal(3, afterRemove.SlotCount);

        var rebuilt = Assert.IsType<Tree>(new RebuildBalancedTreeOperation().Execute(afterRemove).NewState);

        Assert.Equal(2, rebuilt.SlotCount);
    }

    [Fact]
    public void Rebuild_EmptyTree_StaysEmpty()
    {
        var result = new RebuildBalancedTreeOperation().Execute(Tree.Empty());

        Assert.Null(Assert.IsType<Tree>(result.NewState).RootIndex);
        Assert.Equal(new TreeRebuildResult(0, 0, 0), result.Value);
    }

    [Fact]
    public void Rebuild_DoesNotMutateTheInputTree()
    {
        var chain = TreeTestData.Build(1, 2, 3, 4, 5);

        new RebuildBalancedTreeOperation().Execute(chain);

        Assert.Equal(5, TreeShape5Height(chain)); // still the original 5-deep chain
    }

    [Fact]
    public void Rebuild_VeryDeepChain_DoesNotOverflowTheStack()
    {
        var result = new RebuildBalancedTreeOperation().Execute(TreeTestData.RightChain(200_000));

        var summary = Assert.IsType<TreeRebuildResult>(result.Value);
        Assert.Equal(200_000, summary.HeightBefore);
        Assert.Equal(18, summary.HeightAfter); // floor(log2(200000)) + 1
    }

    private static int TreeShape5Height(Tree tree)
    {
        var stats = (TreeStatsResult)new TreeStatsOperation().Execute(tree).Value!;
        return stats.Height;
    }

    // ---------- tree-to-graph ----------

    [Fact]
    public void ToGraph_NumbersLiveNodesInLevelOrder_AndLinksParentsToChildren()
    {
        var result = new TreeToGraphOperation().Execute(TreeTestData.Build(50, 30, 70));

        var graph = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(3, graph.NodeCount);
        Assert.Equal(new[] { 1, 2 }, graph.GetNeighbors(0).OrderBy(x => x));
        Assert.Equal(new[] { 0 }, graph.GetNeighbors(1));
        Assert.Equal(new[] { 0 }, graph.GetNeighbors(2));
    }

    [Fact]
    public void ToGraph_Value_MapsGraphNodesBackToTreeSlotsAndValues()
    {
        var result = new TreeToGraphOperation().Execute(TreeTestData.Build(50, 30, 70));

        var mapping = Assert.IsType<List<TreeNodeMapping>>(result.Value);
        Assert.Equal(new TreeNodeMapping(GraphNode: 0, TreeSlot: 0, Value: 50), mapping[0]);
        Assert.Equal(new TreeNodeMapping(1, 1, 30), mapping[1]);
        Assert.Equal(new TreeNodeMapping(2, 2, 70), mapping[2]);
    }

    [Fact]
    public void ToGraph_SkipsSlotsOfRemovedNodes()
    {
        var afterRemove = (Tree)new RemoveNodeOperation(3).Execute(TreeTestData.Build(5, 3, 8)).NewState!;

        var graph = Assert.IsType<Graph>(new TreeToGraphOperation().Execute(afterRemove).NewState);

        Assert.Equal(2, graph.NodeCount); // the dead slot does not become an isolated node
    }

    [Fact]
    public void ToGraph_EmptyTree_ProducesEmptyGraph()
    {
        var graph = Assert.IsType<Graph>(new TreeToGraphOperation().Execute(Tree.Empty()).NewState);

        Assert.Equal(0, graph.NodeCount);
    }

    [Fact]
    public void ToGraph_VeryDeepChain_Works()
    {
        var graph = Assert.IsType<Graph>(new TreeToGraphOperation().Execute(TreeTestData.RightChain(200_000)).NewState);

        Assert.Equal(200_000, graph.NodeCount);
        Assert.Equal(399_998, graph.EdgeCount); // two directed edges per parent/child link
    }

    [Fact]
    public void ToGraph_ResultWorksWithGraphOperations()
    {
        var graph = Assert.IsType<Graph>(new TreeToGraphOperation().Execute(TreeTestData.Sample()).NewState);

        var reachable = new ReachabilityQuery(startNode: 0).Execute(graph).Value;

        Assert.Equal(7, reachable); // a tree is connected: every node is reachable from the root
    }

    // ---------- generate-tree ----------

    [Fact]
    public void Generate_ProducesTheRequestedNodeCount_InSortedOrder()
    {
        var tree = Assert.IsType<Tree>(new GenerateTreeOperation(count: 100, seed: 1).Execute(Tree.Empty()).NewState);

        var values = Values(tree);
        Assert.Equal(100, values.Count);
        Assert.Equal(values.OrderBy(v => v), values);
    }

    [Fact]
    public void Generate_SameSeed_ProducesTheSameTree()
    {
        var a = Assert.IsType<Tree>(new GenerateTreeOperation(50, seed: 7).Execute(Tree.Empty()).NewState);
        var b = Assert.IsType<Tree>(new GenerateTreeOperation(50, seed: 7).Execute(Tree.Empty()).NewState);

        Assert.Equal(Values(a), Values(b)); // determinism: required for rehydration replay
    }

    [Fact]
    public void Generate_DifferentSeeds_ProduceDifferentTrees()
    {
        var a = Assert.IsType<Tree>(new GenerateTreeOperation(50, seed: 1).Execute(Tree.Empty()).NewState);
        var b = Assert.IsType<Tree>(new GenerateTreeOperation(50, seed: 2).Execute(Tree.Empty()).NewState);

        Assert.NotEqual(Values(a), Values(b));
    }

    [Fact]
    public void Generate_ZeroCount_ProducesAnEmptyTree()
    {
        var tree = Assert.IsType<Tree>(new GenerateTreeOperation(0, seed: 1).Execute(Tree.Empty()).NewState);

        Assert.Null(tree.RootIndex);
    }

    [Fact]
    public void Generate_LargeTree_RetainsBoundedTrace_ButCountsEveryInsert()
    {
        var result = new GenerateTreeOperation(100_000, seed: 1).Execute(Tree.Empty());

        Assert.Equal(TraceRecorder.DefaultMaxEvents, result.Trace.Count);
        Assert.Equal(100_000, TraceSummary.Of(result.Trace).TotalCount);
    }

    [Fact]
    public void Generate_InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new GenerateTreeOperation(-1, seed: 1));
        Assert.Throws<ArgumentException>(() => new GenerateTreeOperation(10, seed: 1, maxValue: 0));
        Assert.Throws<ArgumentException>(() => new GenerateTreeOperation(DatasetLimits.MaxCells + 1, seed: 1));
    }
}