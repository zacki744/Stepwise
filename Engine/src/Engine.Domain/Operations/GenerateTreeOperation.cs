using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

/// <summary>
/// Generation: builds a tree of `count` nodes by inserting seeded-random values in [0, maxValue).
/// Same seed, same tree (required for rehydration replay). Ignores the input tree.
/// </summary>
public sealed class GenerateTreeOperation : IOperation<Tree>
{
    private readonly int _count;
    private readonly int _seed;
    private readonly int _maxValue;

    public GenerateTreeOperation(int count, int seed, int maxValue = 1_000_000)
    {
        if (count < 0) throw new ArgumentException($"count must not be negative (got {count})");
        if (maxValue < 1) throw new ArgumentException($"maxValue must be at least 1 (got {maxValue})");
        DatasetLimits.EnsureWithinLimit(count, $"Generated tree of {count:N0} nodes");
        _count = count;
        _seed = seed;
        _maxValue = maxValue;
    }

    public OperationMetadata Metadata { get; } = new("generate-tree", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var trace = new TraceRecorder(context);
        var random = new Random(_seed);
        var values = new List<int?>(_count);
        var left = new List<int>(_count);
        var right = new List<int>(_count);
        int root = -1;

        for (int i = 0; i < _count; i++)
        {
            int value = random.Next(_maxValue);
            int index = values.Count;
            values.Add(value);
            left.Add(-1);
            right.Add(-1);

            if (root == -1)
            {
                root = index;
                trace.Add(new DomainStepEvent("NodeInserted", $"Inserted {value} as root",
                    new NodeInsertedPayload(value, index, null)));
                continue;
            }

            int current = root;
            while (true)
            {
                context.Checkpoint(); // the walk is the real work, not just the trace
                if (value < values[current]!.Value)
                {
                    if (left[current] == -1) { left[current] = index; break; }
                    current = left[current];
                }
                else
                {
                    if (right[current] == -1) { right[current] = index; break; }
                    current = right[current];
                }
            }

            trace.Add(new DomainStepEvent("NodeInserted", $"Inserted {value} at {index} under {current}",
                new NodeInsertedPayload(value, index, current)));
        }

        return new OperationExecutionResult(true, Tree.FromArrays(values, left, right, root), null, trace, null);
    }
}