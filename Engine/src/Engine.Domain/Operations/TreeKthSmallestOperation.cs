using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record KthSmallestResult(bool Found, int? Value, int? NodeIndex);

/// <summary>k is 1-based; duplicates count separately. A k beyond the tree's size is a result (Found=false), not an error.</summary>
public sealed class TreeKthSmallestOperation : IOperation<Tree>
{
    private readonly int _k;

    public TreeKthSmallestOperation(int k)
    {
        if (k < 1) throw new ArgumentException($"k must be at least 1 (got {k})");
        _k = k;
    }

    public OperationMetadata Metadata { get; } = new("tree-kth-smallest", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var trace = new TraceRecorder(context);
        int position = 0;

        foreach (var slot in input.InOrder())
        {
            position++;
            int value = input.GetValue(slot);
            trace.Add(new DomainStepEvent("TreeNodeVisited", $"Visited {value} (#{position})",
                new TreeNodeVisitedPayload(slot, value, position)));

            if (position == _k)
                return new OperationExecutionResult(true, null, new KthSmallestResult(true, value, slot), trace, null);
        }

        trace.Add(new DomainStepEvent("KOutOfRange", $"The tree has only {position} node(s), so there is no #{_k}"));
        return new OperationExecutionResult(true, null, new KthSmallestResult(false, null, null), trace, null);
    }
}