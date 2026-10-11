using Engine.Domain.Datasets;

namespace Engine.Domain.Operations;

internal static class TreeShape
{
    /// <summary>Number of levels: empty tree = 0, single node = 1. Iterative, so depth can't overflow the stack.</summary>
    public static int Height(Tree tree)
    {
        if (tree.RootIndex is not int root) return 0;

        int levels = 0;
        var queue = new Queue<int>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            levels++;
            for (int remaining = queue.Count; remaining > 0; remaining--)
            {
                int node = queue.Dequeue();
                if (tree.LeftChild(node) is int left) queue.Enqueue(left);
                if (tree.RightChild(node) is int right) queue.Enqueue(right);
            }
        }
        return levels;
    }
}