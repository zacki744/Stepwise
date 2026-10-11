using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record NodePlacedPayload(int Value, int Index, int? ParentIndex, bool IsLeft);
public sealed record TreeRebuildResult(int Count, int HeightBefore, int HeightAfter);

/// <summary>
/// Rebuilds the tree from its in-order values as a minimum-height tree. Duplicates stay on the right
/// (left &lt; node &lt;= right, matching insert), so the height guarantee holds for distinct values.
/// The new tree is also compact: slots left behind by earlier removals are gone.
/// </summary>
public sealed class RebuildBalancedTreeOperation : IOperation<Tree>
{
    public OperationMetadata Metadata { get; } = new("tree-rebuild-balanced", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var trace = new TraceRecorder(context);
        int heightBefore = TreeShape.Height(input);
        int[] sorted = input.InOrder().Select(input.GetValue).ToArray();
        int n = sorted.Length;
        context.Checkpoint(n);

        var values = new List<int?>(n);
        var left = new List<int>(n);
        var right = new List<int>(n);
        int root = -1;

        var work = new Stack<(int Lo, int Hi, int Parent, bool IsLeft)>();
        work.Push((0, n - 1, -1, false));

        while (work.Count > 0)
        {
            var (lo, hi, parent, isLeft) = work.Pop();
            if (lo > hi) continue;

            int mid = lo + (hi - lo) / 2;
            while (mid > lo && sorted[mid - 1] == sorted[mid]) mid--; // keep equal values to the right

            int index = values.Count;
            values.Add(sorted[mid]);
            left.Add(-1);
            right.Add(-1);
            if (parent == -1) root = index;
            else if (isLeft) left[parent] = index;
            else right[parent] = index;

            trace.Add(new DomainStepEvent("NodePlaced", $"Placed {sorted[mid]} at {index}",
                new NodePlacedPayload(sorted[mid], index, parent == -1 ? null : parent, isLeft)));

            work.Push((mid + 1, hi, index, false));
            work.Push((lo, mid - 1, index, true)); // popped first, so the left subtree is built first
        }

        var rebuilt = Tree.FromArrays(values, left, right, root);
        var summary = new TreeRebuildResult(n, heightBefore, TreeShape.Height(rebuilt));
        return new OperationExecutionResult(true, rebuilt, summary, trace, null);
    }
}