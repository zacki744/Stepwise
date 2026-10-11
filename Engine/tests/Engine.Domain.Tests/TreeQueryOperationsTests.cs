using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class TreeQueryOperationsTests
{
    // ---------- tree-contains ----------

    [Fact]
    public void Contains_PresentValue_ReportsSlotAndComparisons()
    {
        var found = Assert.IsType<TreeSearchResult>(new TreeContainsOperation(60).Execute(TreeTestData.Sample()).Value);

        Assert.True(found.Found);
        Assert.Equal(5, found.NodeIndex);   // 60 was the sixth insert
        Assert.Equal(3, found.Comparisons); // 50 -> 70 -> 60
    }

    [Fact]
    public void Contains_MissingValue_ReportsNotFound()
    {
        var found = Assert.IsType<TreeSearchResult>(new TreeContainsOperation(65).Execute(TreeTestData.Sample()).Value);

        Assert.False(found.Found);
        Assert.Null(found.NodeIndex);
        Assert.Equal(3, found.Comparisons); // 50, 70, 60 then off the end
    }

    [Fact]
    public void Contains_EmptyTree_ReportsNotFound()
    {
        var found = Assert.IsType<TreeSearchResult>(new TreeContainsOperation(1).Execute(Tree.Empty()).Value);

        Assert.False(found.Found);
        Assert.Equal(0, found.Comparisons);
    }

    [Fact]
    public void Contains_OnVeryDeepChain_IsInterruptedByTheWorkBudget()
    {
        var context = new OperationContext(CancellationToken.None, null, maxWorkUnits: 100);

        Assert.Throws<OperationAbortedException>(() =>
            new TreeContainsOperation(199_999).Execute(TreeTestData.RightChain(200_000), context));
    }

    // ---------- tree-traverse ----------

    [Theory]
    [InlineData("pre", new[] { 50, 30, 20, 40, 70, 60, 80 })]
    [InlineData("in", new[] { 20, 30, 40, 50, 60, 70, 80 })]
    [InlineData("post", new[] { 20, 40, 30, 60, 80, 70, 50 })]
    [InlineData("level", new[] { 50, 30, 70, 20, 40, 60, 80 })]
    public void Traverse_VisitsNodesInTheExpectedOrder(string order, int[] expected)
    {
        var result = new TraverseTreeOperation(order).Execute(TreeTestData.Sample());

        Assert.Equal(expected, Assert.IsType<List<int>>(result.Value));
    }

    [Theory]
    [InlineData("pre")]
    [InlineData("in")]
    [InlineData("post")]
    [InlineData("level")]
    public void Traverse_EmptyTree_VisitsNothing(string order)
    {
        var result = new TraverseTreeOperation(order).Execute(Tree.Empty());

        Assert.Empty(Assert.IsType<List<int>>(result.Value));
    }

    [Theory]
    [InlineData("pre")]
    [InlineData("in")]
    [InlineData("post")]
    [InlineData("level")]
    public void Traverse_VeryDeepChain_DoesNotOverflowTheStack(string order)
    {
        var result = new TraverseTreeOperation(order).Execute(TreeTestData.RightChain(200_000));

        Assert.Equal(200_000, Assert.IsType<List<int>>(result.Value).Count);
    }

    [Fact]
    public void Traverse_UnknownOrder_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new TraverseTreeOperation("sideways"));
    }

    // ---------- tree-stats ----------

    [Fact]
    public void Stats_SampleTree()
    {
        var stats = Assert.IsType<TreeStatsResult>(new TreeStatsOperation().Execute(TreeTestData.Sample()).Value);

        Assert.Equal(new TreeStatsResult(Count: 7, Min: 20, Max: 80, Height: 3, LeafCount: 4), stats);
    }

    [Fact]
    public void Stats_EmptyTree()
    {
        var stats = Assert.IsType<TreeStatsResult>(new TreeStatsOperation().Execute(Tree.Empty()).Value);

        Assert.Equal(new TreeStatsResult(0, null, null, 0, 0), stats);
    }

    [Fact]
    public void Stats_DeepChain_CountsEveryLevel()
    {
        var stats = Assert.IsType<TreeStatsResult>(new TreeStatsOperation().Execute(TreeTestData.RightChain(200_000)).Value);

        Assert.Equal(200_000, stats.Count);
        Assert.Equal(200_000, stats.Height);
        Assert.Equal(1, stats.LeafCount);
    }

    [Fact]
    public void Stats_CountsLiveNodes_NotAllocatedSlots()
    {
        var tree = TreeTestData.Build(5, 3, 8);
        var afterRemove = (Tree)new RemoveNodeOperation(3).Execute(tree).NewState!;

        var stats = Assert.IsType<TreeStatsResult>(new TreeStatsOperation().Execute(afterRemove).Value);

        Assert.Equal(3, afterRemove.SlotCount); // the removed slot is still allocated
        Assert.Equal(2, stats.Count);
    }

    // ---------- tree-kth-smallest ----------

    [Theory]
    [InlineData(1, 20)]
    [InlineData(4, 50)]
    [InlineData(7, 80)]
    public void Kth_ReturnsTheKthSmallestValue(int k, int expected)
    {
        var result = Assert.IsType<KthSmallestResult>(new TreeKthSmallestOperation(k).Execute(TreeTestData.Sample()).Value);

        Assert.True(result.Found);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Kth_BeyondTreeSize_IsNotFound_NotAnError()
    {
        var result = Assert.IsType<KthSmallestResult>(new TreeKthSmallestOperation(8).Execute(TreeTestData.Sample()).Value);

        Assert.False(result.Found);
    }

    [Fact]
    public void Kth_DuplicatesCountSeparately()
    {
        var tree = TreeTestData.Build(5, 5, 3); // sorted: 3, 5, 5

        var third = Assert.IsType<KthSmallestResult>(new TreeKthSmallestOperation(3).Execute(tree).Value);

        Assert.Equal(5, third.Value);
    }

    [Fact]
    public void Kth_NonPositiveK_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new TreeKthSmallestOperation(0));
    }

    // ---------- tree-lca ----------

    [Theory]
    [InlineData(20, 40, 30)]
    [InlineData(20, 80, 50)]
    [InlineData(60, 80, 70)]
    [InlineData(30, 40, 30)] // a node is its own ancestor
    [InlineData(50, 50, 50)]
    [InlineData(20, 20, 20)]
    public void Lca_FindsTheLowestCommonAncestor(int a, int b, int expectedValue)
    {
        var result = Assert.IsType<LcaResult>(new TreeLowestCommonAncestorOperation(a, b).Execute(TreeTestData.Sample()).Value);

        Assert.True(result.Found);
        Assert.Equal(expectedValue, result.Value);
    }

    [Fact]
    public void Lca_WhenEitherValueIsMissing_IsNotFound()
    {
        var result = Assert.IsType<LcaResult>(new TreeLowestCommonAncestorOperation(20, 999).Execute(TreeTestData.Sample()).Value);

        Assert.False(result.Found);
    }
}