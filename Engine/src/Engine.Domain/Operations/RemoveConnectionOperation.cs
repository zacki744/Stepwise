using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record ConnectionRemovedPayload(int From, int To, bool Directed = false);


/// <summary>
/// Removes the connection between two nodes: both directions by default, or with `directed` only the
/// edges from -> to (every parallel copy), leaving a reverse edge in place. Value reports whether
/// anything was removed.
/// </summary>
public sealed class RemoveConnectionOperation(int from, int to, bool directed = false) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("remove-connection", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        context.Checkpoint(input.NodeCount + (long)input.EdgeCount); // rebuilding the graph copies every edge
        var original = input.AllDirectedEdges().ToList();
        var remaining = original
            .Where(e => !((e.From == from && e.To == to) || (!directed && e.From == to && e.To == from)))
            .ToList();
        bool existed = remaining.Count < original.Count;

        var trace = new TraceRecorder(context);
        trace.Add(existed
            ? new DomainStepEvent("ConnectionRemoved", directed ? $"{from} -> {to}" : $"{from} <-> {to}",
                new ConnectionRemovedPayload(from, to, directed))
            : new DomainStepEvent("ConnectionNotFound",
                directed ? $"No connection from {from} to {to}" : $"No connection between {from} and {to}"));

        return new OperationExecutionResult(true, new Graph(input.NodeCount, remaining), existed, trace, null);
    }
}