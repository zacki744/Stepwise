using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class TreeOperationsTests
{
    private static Tree InsertAll(params int[] values)
    {
        var tree = Tree.Empty();
        foreach (var v in values)
            tree = (Tree)new InsertNodeOperation(v).Execute(tree).NewState!;
        return tree;
    }

    [Fact]
    public void Insert_SingleValue_BecomesRoot()
    {
        var tree = InsertAll(50);
        Assert.Equal(0, tree.RootIndex);
        Assert.Equal(50, tree.GetValue(tree.RootIndex!.Value));
    }

    [Fact]
    public void Insert_MultipleValues_InOrderTraversalIsSorted()
    {
        var tree = InsertAll(50, 30, 70, 20, 40, 60, 80);
        var sorted = tree.InOrder().Select(tree.GetValue).ToList();
        Assert.Equal([20, 30, 40, 50, 60, 70, 80], sorted);
    }

    [Fact]
    public void Insert_DoesNotMutateInputTree()
    {
        var original = InsertAll(50);
        new InsertNodeOperation(30).Execute(original);
        Assert.Single(original.InOrder()); // original still has exactly the one node
    }

    [Fact]
    public void Remove_LeafNode_RemovesItCleanly()
    {
        var tree = InsertAll(50, 30, 70);
        var result = new RemoveNodeOperation(30).Execute(tree);

        Assert.True((bool)result.Value!);
        var updated = Assert.IsType<Tree>(result.NewState);
        Assert.Equal([50, 70], updated.InOrder().Select(updated.GetValue).ToList());
    }

    [Fact]
    public void Remove_NodeWithOneChild_SplicesChildIntoParent()
    {
        var tree = InsertAll(50, 30, 20); // 30 has only a left child (20)
        var result = new RemoveNodeOperation(30).Execute(tree);

        var updated = Assert.IsType<Tree>(result.NewState);
        Assert.Equal([20, 50], updated.InOrder().Select(updated.GetValue).ToList());
    }

    [Fact]
    public void Remove_NodeWithTwoChildren_ReplacesWithInOrderSuccessor()
    {
        var tree = InsertAll(50, 30, 70, 60, 80); // 70 has two children: 60, 80
        var result = new RemoveNodeOperation(70).Execute(tree);

        var updated = Assert.IsType<Tree>(result.NewState);
        Assert.Equal([30, 50, 60, 80], updated.InOrder().Select(updated.GetValue).ToList());
    }

    [Fact]
    public void Remove_Root_WithTwoChildren_TreeRemainsValidBst()
    {
        var tree = InsertAll(50, 30, 70, 20, 40, 60, 80);
        var result = new RemoveNodeOperation(50).Execute(tree);

        var updated = Assert.IsType<Tree>(result.NewState);
        Assert.Equal([20, 30, 40, 60, 70, 80], updated.InOrder().Select(updated.GetValue).ToList());
    }

    [Fact]
    public void Remove_ValueNotPresent_ReturnsFalse_StructureUnchanged()
    {
        var tree = InsertAll(50, 30, 70);
        var result = new RemoveNodeOperation(999).Execute(tree);

        Assert.False((bool)result.Value!);
        var updated = Assert.IsType<Tree>(result.NewState);
        Assert.Equal([30, 50, 70], updated.InOrder().Select(updated.GetValue).ToList());
    }

    [Fact]
    public void Remove_DoesNotMutateInputTree()
    {
        var tree = InsertAll(50, 30, 70);
        new RemoveNodeOperation(30).Execute(tree);
        Assert.Equal([30, 50, 70], tree.InOrder().Select(tree.GetValue).ToList()); // original untouched
    }
}