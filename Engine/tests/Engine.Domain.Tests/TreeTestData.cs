using Engine.Domain.Datasets;
using Engine.Domain.Operations;

namespace Engine.Domain.Tests;

internal static class TreeTestData
{
    public static Tree Build(params int[] values)
    {
        var tree = Tree.Empty();
        foreach (var v in values)
            tree = (Tree)new InsertNodeOperation(v).Execute(tree).NewState!;
        return tree;
    }

    //        50          slots: 50=0, 30=1, 70=2, 20=3, 40=4, 60=5, 80=6
    //      30    70
    //     20 40 60 80
    public static Tree Sample() => Build(50, 30, 70, 20, 40, 60, 80);

    /// <summary>A degenerate right-leaning chain 0 -> 1 -> ... -> n-1, built directly (sorted inserts would take O(n^2)).</summary>
    public static Tree RightChain(int n)
    {
        var values = Enumerable.Range(0, n).Select(i => (int?)i).ToList();
        var left = Enumerable.Repeat(-1, n).ToList();
        var right = Enumerable.Range(1, n).Select(next => next < n ? next : -1).ToList();
        return Tree.FromArrays(values, left, right, root: 0);
    }
}