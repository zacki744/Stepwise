using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record ConnectionAddedPayload(int From, int To, double Weight);

/// <summary>
/// Adds an undirected connection (both directions) between two nodes.
/// No-op if the connection already exists — Value reports whether a new
/// connection was actually added, so a client can distinguish "added" from
/// "already there" without a separate lookup.
/// </summary>
public sealed class AddConnectionOperation(int from, int to, double weight = 1.0) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("add-connection", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        input.EnsureNode(from, nameof(from));
        input.EnsureNode(to, nameof(to));
        context.Checkpoint(input.NodeCount + (long)input.EdgeCount); // rebuilding the graph copies every edge
        var trace = new TraceRecorder(context);

        if (input.GetNeighbors(from).Contains(to))
        {
            trace.Add(new DomainStepEvent("ConnectionAlreadyExists", $"{from} <-> {to} already connected"));
            return new OperationExecutionResult(true, input, false, trace, null);
        }

        var edges = input.AllDirectedEdges().ToList();
        edges.Add((from, to, weight));
        edges.Add((to, from, weight));

        trace.Add(new DomainStepEvent("ConnectionAdded", $"{from} <-> {to} (weight {weight})",
            new ConnectionAddedPayload(from, to, weight)));

        return new OperationExecutionResult(true, new Graph(input.NodeCount, edges), true, trace, null);
    }
}