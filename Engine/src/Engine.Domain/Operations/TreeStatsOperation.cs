using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record LevelScannedPayload(int Level, int NodeCount);

/// <summary>Height counts levels (empty = 0, single node = 1). Counts LIVE nodes, not allocated slots.</summary>
public sealed record TreeStatsResult(int Count, int? Min, int? Max, int Height, int LeafCount);

public sealed class TreeStatsOperation : IOperation<Tree>
{
    public OperationMetadata Metadata { get; } = new("tree-stats", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var trace = new TraceRecorder(context);

        if (input.RootIndex is not int root)
        {
            trace.Add(new DomainStepEvent("TreeEmpty", "The tree has no nodes"));
            return new OperationExecutionResult(true, null, new TreeStatsResult(0, null, null, 0, 0), trace, null);
        }

        int count = 0, leaves = 0, levels = 0;
        var queue = new Queue<int>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            levels++;
            int width = queue.Count;
            for (int i = 0; i < width; i++)
            {
                int node = queue.Dequeue();
                count++;
                var left = input.LeftChild(node);
                var right = input.RightChild(node);
                if (left is null && right is null) leaves++;
                if (left is int l) queue.Enqueue(l);
                if (right is int r) queue.Enqueue(r);
            }
            trace.Add(new DomainStepEvent("LevelScanned", $"Level {levels}: {width} node(s)",
                new LevelScannedPayload(levels, width)));
        }

        int minNode = root;
        while (input.LeftChild(minNode) is int smaller) minNode = smaller;
        int maxNode = root;
        while (input.RightChild(maxNode) is int larger) maxNode = larger;

        var stats = new TreeStatsResult(count, input.GetValue(minNode), input.GetValue(maxNode), levels, leaves);
        return new OperationExecutionResult(true, null, stats, trace, null);
    }
}