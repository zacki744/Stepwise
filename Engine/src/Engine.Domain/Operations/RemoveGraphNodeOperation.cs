using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record GraphNodeRemovedPayload(int Node, int EdgesRemoved);

/// <param name="RemovedNode">The node that was removed (its index before the removal).</param>
/// <param name="EdgesRemoved">Directed edges that touched it (a self-loop counts once).</param>
/// <param name="NewNodeCount">Nodes left.</param>
/// <param name="OldOfNew">
/// OldOfNew[i] is the old index of the node that is now numbered i. Only the first
/// DatasetLimits.MaxReturnedItems entries are listed (Truncated); the rule is always
/// old = new &lt; RemovedNode ? new : new + 1.
/// </param>
public sealed record RemoveGraphNodeResult(
    int RemovedNode, int EdgesRemoved, int NewNodeCount, IReadOnlyList<int> OldOfNew, bool Truncated);

/// <summary>
/// Really deletes a node: its connections go with it and every higher index shifts down by one, so the
/// graph has one node fewer. Contrast isolate-node, which keeps the index and only strips the
/// connections. Because indices change, the result carries the old-to-new mapping a client needs to
/// translate any node numbers it holds. Registered as "remove-graph-node" (remove-node is the tree's).
/// </summary>
public sealed class RemoveGraphNodeOperation(int node) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("remove-graph-node", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        input.EnsureNode(node, nameof(node));
        context.Checkpoint(input.NodeCount + (long)input.EdgeCount); // rebuilding the graph copies every edge

        var trace = new TraceRecorder(context);
        var kept = new List<(int From, int To, double Weight)>();
        int removed = 0;

        foreach (var (from, to, weight) in input.AllDirectedEdges())
        {
            if (from == node || to == node)
            {
                removed++;
                continue;
            }
            kept.Add((from > node ? from - 1 : from, to > node ? to - 1 : to, weight));
        }

        trace.Add(new DomainStepEvent("GraphNodeRemoved",
            $"Removed node {node} and {removed} connection(s); nodes above it shift down by one",
            new GraphNodeRemovedPayload(node, removed)));

        int newCount = input.NodeCount - 1;
        int listed = Math.Min(newCount, DatasetLimits.MaxReturnedItems);
        var oldOfNew = new int[listed];
        for (int i = 0; i < listed; i++) oldOfNew[i] = i < node ? i : i + 1;

        return new OperationExecutionResult(true, new Graph(newCount, kept),
            new RemoveGraphNodeResult(node, removed, newCount, oldOfNew, listed < newCount), trace, null);
    }
}
