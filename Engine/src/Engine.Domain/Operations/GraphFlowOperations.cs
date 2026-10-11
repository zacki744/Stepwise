using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record FlowAugmentedPayload(double Amount, int PathLength, double TotalFlow);
public sealed record FlowEdge(int From, int To, double Capacity, double Flow);
public sealed record MaxFlowResult(double Value, IReadOnlyList<FlowEdge> Flows, IReadOnlyList<FlowEdge> MinCut, bool Truncated);

public sealed record MatchedPair(int Left, int Right);
public sealed record MatchingResult(bool IsBipartite, int MatchingSize, IReadOnlyList<MatchedPair> Pairs, bool Truncated);

/// <summary>
/// Maximum flow from source to sink (Dinic), using edge weights as capacities and following edges as
/// stored. Capacities must be finite and non-negative. Value is the flow; Flows lists the edges carrying
/// some; MinCut lists the saturated edges crossing the minimum cut (their capacities sum to Value).
/// Each list is capped at DatasetLimits.MaxReturnedItems (Truncated).
/// </summary>
public sealed class MaxFlowOperation(int source, int sink) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("max-flow", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        input.EnsureNode(source, nameof(source));
        input.EnsureNode(sink, nameof(sink));
        if (source == sink) throw new ArgumentException("source and sink must be different nodes");
        context.Checkpoint(input.NodeCount + (long)input.EdgeCount);

        var network = new FlowNetwork(input.NodeCount, input.EdgeCount);
        foreach (var (from, to, weight) in input.AllDirectedEdges())
        {
            if (!double.IsFinite(weight) || weight < 0)
                throw new ArgumentException("max-flow needs finite, non-negative edge capacities (weights)");
            if (from != to) network.AddEdge(from, to, weight); // a self-loop can never carry flow
        }

        var trace = new TraceRecorder(context);
        double value = network.MaxFlow(source, sink, context, (amount, length, total) =>
            trace.Add(new DomainStepEvent("FlowAugmented",
                $"Pushed {amount:0.####} along a path of {length} edge(s); total {total:0.####}",
                new FlowAugmentedPayload(amount, length, total))));

        var reachable = network.ResidualReachableFrom(source);
        var flows = new List<FlowEdge>();
        var cut = new List<FlowEdge>();
        bool truncated = false;

        for (int edge = 0; edge < network.EdgeCount; edge++)
        {
            int from = network.From(edge), to = network.To(edge);
            double capacity = network.Capacity(edge), flow = network.Flow(edge);

            if (flow > FlowNetwork.Epsilon)
            {
                if (flows.Count < DatasetLimits.MaxReturnedItems) flows.Add(new FlowEdge(from, to, capacity, flow));
                else truncated = true;
            }
            if (reachable[from] && !reachable[to] && capacity > FlowNetwork.Epsilon)
            {
                if (cut.Count < DatasetLimits.MaxReturnedItems) cut.Add(new FlowEdge(from, to, capacity, flow));
                else truncated = true;
            }
        }

        return new OperationExecutionResult(true, null, new MaxFlowResult(value, flows, cut, truncated), trace, null);
    }
}

/// <summary>
/// Maximum-cardinality matching in a bipartite graph, by reduction to max-flow with unit capacities.
/// Direction is ignored. The two sides are found by two-colouring (colour 0 = Left); if the graph is not
/// bipartite the result says so. Pairs are sorted by Left. Cardinality only: a weighted (cheapest)
/// assignment is a different problem.
/// </summary>
public sealed class BipartiteMatchingOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("bipartite-matching", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        context.Checkpoint(n + (long)input.EdgeCount);
        var adjacency = new UndirectedAdjacency(input);
        var (colors, conflictFrom, conflictTo) = BipartiteColoring.Compute(adjacency, context);
        var trace = new TraceRecorder(context);

        if (colors is null)
        {
            trace.Add(new DomainStepEvent("NotBipartite", $"Nodes {conflictFrom} and {conflictTo} are joined but would share a side"));
            return new OperationExecutionResult(true, null, new MatchingResult(false, 0, [], false), trace, null);
        }

        long middle = 0;
        for (int u = 0; u < n; u++)
            if (colors[u] == 0) middle += adjacency.Degree(u);

        // Edges 0..middleEdges-1 are the candidate pairs (left -> right); the rest connect source and sink.
        var network = new FlowNetwork(n + 2, checked((int)middle) + n);
        int source = n, sink = n + 1;
        for (int u = 0; u < n; u++)
            if (colors[u] == 0)
                for (int i = 0; i < adjacency.Degree(u); i++)
                    network.AddEdge(u, adjacency.NeighborAt(u, i), 1);
        int middleEdges = network.EdgeCount;
        for (int u = 0; u < n; u++)
        {
            if (colors[u] == 0) network.AddEdge(source, u, 1);
            else network.AddEdge(u, sink, 1);
        }

        int size = (int)Math.Round(network.MaxFlow(source, sink, context));

        var pairs = new List<MatchedPair>();
        for (int edge = 0; edge < middleEdges; edge++)
        {
            if (network.Flow(edge) <= 0.5) continue;
            var pair = new MatchedPair(network.From(edge), network.To(edge));
            trace.Add(new DomainStepEvent("PairMatched", $"{pair.Left} - {pair.Right}", pair));
            if (pairs.Count < DatasetLimits.MaxReturnedItems) pairs.Add(pair);
        }

        return new OperationExecutionResult(true, null, new MatchingResult(true, size, pairs, size > pairs.Count), trace, null);
    }
}