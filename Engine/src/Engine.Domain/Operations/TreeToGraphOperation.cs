using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record TreeNodeMapping(int GraphNode, int TreeSlot, int Value);
public sealed record TreeNodeMappedPayload(int GraphNode, int TreeSlot, int Value);

/// <summary>
/// Convert: Tree -> Graph. Only LIVE nodes become graph nodes, numbered in level order (the root is
/// node 0). Parent/child links become undirected weight-1 edges. Value maps each graph node back to
/// its tree slot and value. Afterwards the session holds a Graph, so every graph operation applies.
/// </summary>
public sealed class TreeToGraphOperation : IOperation<Tree>
{
    public OperationMetadata Metadata { get; } = new("tree-to-graph", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var trace = new TraceRecorder(context);
        var mapping = new List<TreeNodeMapping>();
        var edges = new List<(int From, int To, double Weight)>();
        var queue = new Queue<(int Slot, int GraphNode)>();

        int Admit(int slot)
        {
            int graphNode = mapping.Count;
            int value = input.GetValue(slot);
            mapping.Add(new TreeNodeMapping(graphNode, slot, value));
            trace.Add(new DomainStepEvent("TreeNodeMapped", $"Tree slot {slot} ({value}) -> graph node {graphNode}",
                new TreeNodeMappedPayload(graphNode, slot, value)));
            queue.Enqueue((slot, graphNode));
            return graphNode;
        }

        if (input.RootIndex is int root) Admit(root);

        while (queue.Count > 0)
        {
            var (slot, parentNode) = queue.Dequeue();
            foreach (var child in input.GetNeighbors(slot))
            {
                int childNode = Admit(child);
                edges.Add((parentNode, childNode, 1.0));
                edges.Add((childNode, parentNode, 1.0));
            }
        }

        return new OperationExecutionResult(true, new Graph(mapping.Count, edges), mapping, trace, null);
    }
}