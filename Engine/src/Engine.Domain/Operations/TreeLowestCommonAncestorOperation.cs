using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record LcaResult(bool Found, int? NodeIndex, int? Value);

/// <summary>
/// LCA of the first nodes (on the search path) holding values a and b. Both values must be in the
/// tree; if either is missing the result is Found=false. A node can be its own ancestor.
/// </summary>
public sealed class TreeLowestCommonAncestorOperation(int a, int b) : IOperation<Tree>
{
    public OperationMetadata Metadata { get; } = new("tree-lca", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var trace = new TraceRecorder(context);

        if (!Exists(input, a, context) || !Exists(input, b, context))
        {
            trace.Add(new DomainStepEvent("ValueMissing", $"Both {a} and {b} must be in the tree"));
            return new OperationExecutionResult(true, null, new LcaResult(false, null, null), trace, null);
        }

        int low = Math.Min(a, b);
        int high = Math.Max(a, b);
        int? current = input.RootIndex;

        // Walk down until the two values no longer fit in the same subtree: that node is the split point.
        while (current is int node)
        {
            int nodeValue = input.GetValue(node);
            trace.Add(new DomainStepEvent("NodeCompared", $"Compared [{low}, {high}] with {nodeValue} at {node}",
                new NodeComparedPayload(node, nodeValue)));

            if (high < nodeValue) current = input.LeftChild(node);
            else if (low > nodeValue) current = input.RightChild(node);
            else return new OperationExecutionResult(true, null, new LcaResult(true, node, nodeValue), trace, null);
        }

        // Unreachable while both values exist; reported as not-found rather than throwing.
        return new OperationExecutionResult(true, null, new LcaResult(false, null, null), trace, null);
    }

    private static bool Exists(Tree tree, int value, OperationContext context)
    {
        int? current = tree.RootIndex;
        while (current is int node)
        {
            context.Checkpoint();
            int nodeValue = tree.GetValue(node);
            if (value == nodeValue) return true;
            current = value < nodeValue ? tree.LeftChild(node) : tree.RightChild(node);
        }
        return false;
    }
}