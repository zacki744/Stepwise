using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record TreeSearchResult(bool Found, int? NodeIndex, int Comparisons);

/// <summary>Finds the first node on the search path holding the value (equal values live to the right).</summary>
public sealed class TreeContainsOperation(int value) : IOperation<Tree>
{
    public OperationMetadata Metadata { get; } = new("tree-contains", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var trace = new TraceRecorder(context);
        int comparisons = 0;
        int? current = input.RootIndex;

        while (current is int node)
        {
            comparisons++;
            int nodeValue = input.GetValue(node);
            trace.Add(new DomainStepEvent("NodeCompared", $"Compared {value} with {nodeValue} at {node}",
                new NodeComparedPayload(node, nodeValue)));

            if (value == nodeValue)
                return new OperationExecutionResult(true, null, new TreeSearchResult(true, node, comparisons), trace, null);

            current = value < nodeValue ? input.LeftChild(node) : input.RightChild(node);
        }

        trace.Add(new DomainStepEvent("NotFound", $"{value} is not in the tree"));
        return new OperationExecutionResult(true, null, new TreeSearchResult(false, null, comparisons), trace, null);
    }
}