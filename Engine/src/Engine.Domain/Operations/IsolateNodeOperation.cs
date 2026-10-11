using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record EdgeRemovedForIsolationPayload(int From, int To);

/// <summary>
/// "Removes" a node by stripping every connection to/from it — the node's
/// index remains valid and addressable, it simply ends up with zero
/// neighbors. This deliberately avoids renumbering node indices (which
/// would invalidate any client-held references to other nodes), the same
/// append-only philosophy RemoveNodeOperation uses for Tree. Returns the
/// number of edges removed, so a client can distinguish "actually had
/// connections" from "was already isolated."
/// </summary>
public sealed class IsolateNodeOperation(int nodeIndex) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("isolate-node", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        if (nodeIndex < 0 || nodeIndex >= input.NodeCount)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));

        var trace = new TraceRecorder(context);
        var remaining = new List<(int From, int To, double Weight)>();
        int removedCount = 0;

        foreach (var (from, to, weight) in input.AllDirectedEdges())
        {
            if (from == nodeIndex || to == nodeIndex)
            {
                removedCount++;
                trace.Add(new DomainStepEvent("EdgeRemovedForIsolation", $"{from} -> {to}",
                    new EdgeRemovedForIsolationPayload(from, to)));
            }
            else
            {
                remaining.Add((from, to, weight));
            }
        }

        if (removedCount == 0)
            trace.Add(new DomainStepEvent("NodeAlreadyIsolated", $"Node {nodeIndex} had no connections"));

        // removedCount is directed-edge count (each undirected connection
        // counted twice); Value reports undirected connections actually severed.
        return new OperationExecutionResult(true, new Graph(input.NodeCount, remaining), removedCount / 2, trace, null);
    }
}