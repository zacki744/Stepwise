using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class TreeSnapshotTests
{
    //        5          slots: 5=0, 3=1, 8=2 ; slot 3 is dead
    //      3   8
    private static List<TreeNodeData> Valid() => new()
    {
        new(0, 5, 1, 2), new(1, 3, -1, -1), new(2, 8, -1, -1)
    };

    // ---------- FromValues ----------

    [Fact]
    public void FromValues_MatchesRepeatedInsertNode()
    {
        var values = new[] { 50, 30, 70, 20, 40, 60, 80, 30, 50 };

        var built = Tree.FromValues(values);
        var inserted = TreeTestData.Build(values);

        Assert.Equal(inserted.LiveNodes(), built.LiveNodes());
        Assert.Equal(inserted.SlotCount, built.SlotCount);
    }

    [Fact]
    public void FromValues_EqualValueGoesRight()
    {
        var tree = Tree.FromValues(new[] { 5, 5 });
        Assert.Null(tree.LeftChild(tree.RootIndex!.Value));
        Assert.Equal(5, tree.GetValue(tree.RightChild(tree.RootIndex!.Value)!.Value));
    }

    [Fact]
    public void FromValues_Empty_IsAnEmptyTree()
    {
        var tree = Tree.FromValues(Array.Empty<int>());
        Assert.Null(tree.RootIndex);
        Assert.Empty(tree.LiveNodes());
    }

    [Fact]
    public void FromValues_LongSortedRun_IsRejectedInsteadOfHangingTheServer()
    {
        // 100,000 ascending values would need about 5 billion pointer steps.
        var sorted = Enumerable.Range(0, 100_000);
        Assert.Throws<ArgumentException>(() => Tree.FromValues(sorted));
    }

    [Fact]
    public void FromValues_LargeShuffledInput_IsFine()
    {
        var random = new DeterministicRandom(1);
        var values = Enumerable.Range(0, 100_000).Select(_ => random.Next(1_000_000)).ToArray();

        Assert.Equal(100_000, Tree.FromValues(values).LiveNodes().Count);
    }

    [Fact]
    public void FromValues_BeyondTheSizeLimit_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            Tree.FromValues(Enumerable.Range(0, DatasetLimits.MaxCells + 1).Select(i => i % 2)));

    // ---------- LiveNodes ----------

    [Fact]
    public void LiveNodes_IsInSlotOrder_AndSkipsRemovedNodes()
    {
        var tree = TreeTestData.Sample(); // 50=0, 30=1, 70=2, 20=3, 40=4, 60=5, 80=6
        tree = (Tree)new RemoveNodeOperation(20).Execute(tree).NewState!; // a leaf: its slot becomes dead

        var live = tree.LiveNodes();

        Assert.Equal(new[] { 0, 1, 2, 4, 5, 6 }, live.Select(n => n.Slot));
        Assert.Equal(7, tree.SlotCount); // the dead slot is still counted
    }

    // ---------- FromNodes ----------

    [Fact]
    public void FromNodes_RoundTripsLiveNodes()
    {
        var tree = Tree.FromNodes(0, Valid());
        Assert.Equal(Valid(), tree.LiveNodes());
        Assert.Equal(new[] { 3, 5, 8 }, tree.InOrder().Select(tree.GetValue));
    }

    [Fact]
    public void FromNodes_PreservesDeadSlotsAndSlotCount()
    {
        var original = TreeTestData.Sample();
        original = (Tree)new RemoveNodeOperation(20).Execute(original).NewState!;

        var rebuilt = Tree.FromNodes(original.RootIndex!.Value, original.LiveNodes(), original.SlotCount);

        Assert.Equal(original.SlotCount, rebuilt.SlotCount);
        Assert.Equal(original.LiveNodes(), rebuilt.LiveNodes());

        // The next insert lands in the same new slot on both.
        var a = (Tree)new InsertNodeOperation(25).Execute(original).NewState!;
        var b = (Tree)new InsertNodeOperation(25).Execute(rebuilt).NewState!;
        Assert.Equal(a.LiveNodes(), b.LiveNodes());
    }

    [Fact]
    public void FromNodes_EmptyTree_RequiresNoRoot()
    {
        Assert.Null(Tree.FromNodes(-1, new List<TreeNodeData>()).RootIndex);
        Assert.Throws<ArgumentException>(() => Tree.FromNodes(0, new List<TreeNodeData>()));
    }

    public static IEnumerable<object[]> Corruptions() => new[]
    {
        new object[] { "duplicate slot",   0, new[] { new TreeNodeData(0, 5, -1, -1), new TreeNodeData(0, 6, -1, -1) } },
        new object[] { "negative slot",    0, new[] { new TreeNodeData(-1, 5, -1, -1) } },
        new object[] { "root missing",     7, new[] { new TreeNodeData(0, 5, -1, -1) } },
        new object[] { "child missing",    0, new[] { new TreeNodeData(0, 5, 9, -1) } },
        new object[] { "shared parent",    0, new[] { new TreeNodeData(0, 5, 1, 1), new TreeNodeData(1, 3, -1, -1) } },
        new object[] { "root has parent",  0, new[] { new TreeNodeData(0, 5, 1, -1), new TreeNodeData(1, 3, 0, -1) } },
        new object[] { "equal value left", 0, new[] { new TreeNodeData(0, 5, 1, -1), new TreeNodeData(1, 5, -1, -1) } },
        new object[] { "right too small", 0, new[] { new TreeNodeData(0, 5, -1, 1), new TreeNodeData(1, 4, -1, -1) } },
        new object[] { "deep order break", 0, new[] { new TreeNodeData(0, 5, 1, -1), new TreeNodeData(1, 3, -1, 2), new TreeNodeData(2, 9, -1, -1) } },
        new object[] { "unreachable",      0, new[] { new TreeNodeData(0, 5, -1, -1), new TreeNodeData(1, 7, -1, -1) } },
    };

    [Theory]
    [MemberData(nameof(Corruptions))]
    public void FromNodes_RejectsCorruptStructures(string reason, int root, TreeNodeData[] nodes)
    {
        _ = reason;
        Assert.Throws<ArgumentException>(() => Tree.FromNodes(root, nodes));
    }

    [Fact]
    public void FromNodes_SlotCountBelowHighestSlot_Throws() =>
        Assert.Throws<ArgumentException>(() => Tree.FromNodes(0, Valid(), slotCount: 2));

    [Fact]
    public void FromNodes_DeepChain_DoesNotOverflowTheStack()
    {
        var nodes = Enumerable.Range(0, 100_000)
            .Select(i => new TreeNodeData(i, i, -1, i + 1 < 100_000 ? i + 1 : -1)).ToList();

        Assert.Equal(100_000, Tree.FromNodes(0, nodes).LiveNodes().Count);
    }

    [Fact]
    public void EveryTreeBuiltByOperations_PassesTheValidator()
    {
        var random = new DeterministicRandom(7);
        var tree = Tree.Empty();
        for (int i = 0; i < 300; i++)
        {
            int value = random.Next(40);
            tree = random.Next(3) == 0
                ? (Tree)new RemoveNodeOperation(value).Execute(tree).NewState!
                : (Tree)new InsertNodeOperation(value).Execute(tree).NewState!;

            var rebuilt = Tree.FromNodes(tree.RootIndex ?? -1, tree.LiveNodes(), tree.SlotCount);
            Assert.Equal(tree.LiveNodes(), rebuilt.LiveNodes());
        }
    }
}
