using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record NodeAddedPayload(int Node);
public sealed record NodesAddedResult(int FirstNode, int Count);
public sealed record EdgeWeightChangedPayload(int From, int To, double OldWeight, double NewWeight);
public sealed record EdgesClearedPayload(int EdgesRemoved, int NodesRemoved);
public sealed record ClearGraphResult(int EdgesRemoved, int NodesRemoved);
public sealed record EdgesReversedPayload(int EdgeCount);
public sealed record NodeKeptPayload(int NewIndex, int OriginalIndex);
public sealed record SubgraphResult(IReadOnlyList<int> OriginalNodes, int EdgesKept);

/// <summary>Appends `count` isolated nodes. Existing node indices never change. Value reports the first new index.</summary>
public sealed class AddNodeOperation : IOperation<Graph>
{
    private readonly int _count;

    public AddNodeOperation(int count = 1)
    {
        if (count < 1) throw new ArgumentException($"count must be at least 1 (got {count})");
        _count = count;
    }

    public OperationMetadata Metadata { get; } = new("add-node", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        long newTotal = (long)input.NodeCount + _count; // 64-bit, so a huge count can't wrap around the limit check
        DatasetLimits.EnsureWithinLimit(newTotal, $"Graph with {newTotal:N0} nodes");
        context.Checkpoint(input.EdgeCount + (long)_count);

        var trace = new TraceRecorder(context);
        for (int i = 0; i < _count; i++)
        {
            int node = input.NodeCount + i;
            trace.Add(new DomainStepEvent("NodeAdded", $"Added node {node}", new NodeAddedPayload(node)));
        }

        var grown = new Graph((int)newTotal, input.AllDirectedEdges());
        return new OperationExecutionResult(true, grown, new NodesAddedResult(input.NodeCount, _count), trace, null);
    }
}

/// <summary>
/// Sets the weight of the directed edge from -> to (every parallel copy of it). With `symmetric`, the
/// reverse edge is updated too, which is what you want for an undirected connection. Value is how many
/// directed edges matched; 0 means no such edge existed and the graph is returned untouched.
/// </summary>
public sealed class SetEdgeWeightOperation(int from, int to, double weight, bool symmetric = false) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("set-edge-weight", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        input.EnsureNode(from, nameof(from));
        input.EnsureNode(to, nameof(to));
        if (!double.IsFinite(weight)) throw new ArgumentException("weight must be a finite number");
        context.Checkpoint(input.EdgeCount);

        var trace = new TraceRecorder(context);
        var edges = new List<(int From, int To, double Weight)>(input.EdgeCount);
        int matched = 0;

        foreach (var (f, t, w) in input.AllDirectedEdges())
        {
            bool isTarget = (f == from && t == to) || (symmetric && f == to && t == from);
            if (!isTarget)
            {
                edges.Add((f, t, w));
                continue;
            }

            matched++;
            edges.Add((f, t, weight));
            if (w != weight)
                trace.Add(new DomainStepEvent("EdgeWeightChanged", $"{f} -> {t}: {w} -> {weight}",
                    new EdgeWeightChangedPayload(f, t, w, weight)));
        }

        if (matched == 0)
        {
            trace.Add(new DomainStepEvent("EdgeNotFound", $"There is no edge from {from} to {to}"));
            return new OperationExecutionResult(true, input, 0, trace, null);
        }

        return new OperationExecutionResult(true, new Graph(input.NodeCount, edges), matched, trace, null);
    }
}

/// <summary>
/// Destruction. Removes every edge and keeps the nodes, or with `removeNodes` empties the graph
/// completely. Value reports how many directed edges and nodes were removed.
/// </summary>
public sealed class ClearEdgesOperation(bool removeNodes = false) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("clear-edges", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int edgesRemoved = input.EdgeCount;
        int nodesRemoved = removeNodes ? input.NodeCount : 0;
        context.Checkpoint(edgesRemoved + (long)input.NodeCount);

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("EdgesCleared", $"Removed {edgesRemoved} edge(s) and {nodesRemoved} node(s)",
            new EdgesClearedPayload(edgesRemoved, nodesRemoved)));

        var cleared = Graph.Empty(removeNodes ? 0 : input.NodeCount);
        return new OperationExecutionResult(true, cleared, new ClearGraphResult(edgesRemoved, nodesRemoved), trace, null);
    }
}

/// <summary>Flips the direction of every edge, keeping weights. An undirected (symmetric) graph is unchanged.</summary>
public sealed class ReverseEdgesOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("reverse-edges", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        context.Checkpoint(input.EdgeCount);

        var reversed = new List<(int From, int To, double Weight)>(input.EdgeCount);
        foreach (var (from, to, weight) in input.AllDirectedEdges())
            reversed.Add((to, from, weight));

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("EdgesReversed", $"Reversed {input.EdgeCount} edge(s)",
            new EdgesReversedPayload(input.EdgeCount)));
        return new OperationExecutionResult(true, new Graph(input.NodeCount, reversed), null, trace, null);
    }
}

/// <summary>
/// The induced subgraph on the given nodes: those nodes, and every edge whose two ends are both kept.
/// Duplicates and order in the request are ignored (set semantics). The result is renumbered 0..k-1 in
/// ascending original order; Value.OriginalNodes[i] is the original index of new node i.
/// </summary>
public sealed class SubgraphOperation : IOperation<Graph>
{
    private readonly int[] _nodes;

    public SubgraphOperation(IReadOnlyList<int> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        _nodes = nodes.Distinct().OrderBy(n => n).ToArray();
    }

    public OperationMetadata Metadata { get; } = new("subgraph", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        foreach (var node in _nodes) input.EnsureNode(node, "nodes");
        context.Checkpoint(input.NodeCount + (long)input.EdgeCount);

        var trace = new TraceRecorder(context);
        var newIndex = new int[input.NodeCount];
        Array.Fill(newIndex, -1);
        for (int i = 0; i < _nodes.Length; i++)
        {
            newIndex[_nodes[i]] = i;
            trace.Add(new DomainStepEvent("NodeKept", $"Kept node {_nodes[i]} as {i}", new NodeKeptPayload(i, _nodes[i])));
        }

        var edges = new List<(int From, int To, double Weight)>();
        foreach (var (from, to, weight) in input.AllDirectedEdges())
            if (newIndex[from] >= 0 && newIndex[to] >= 0)
                edges.Add((newIndex[from], newIndex[to], weight));

        return new OperationExecutionResult(true, new Graph(_nodes.Length, edges),
            new SubgraphResult(_nodes, edges.Count), trace, null);
    }
}