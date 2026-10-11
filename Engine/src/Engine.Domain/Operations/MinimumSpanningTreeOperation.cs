using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record EdgeConsideredPayload(int From, int To, double Weight);
public sealed record EdgeAddedPayload(int From, int To, double Weight);
public sealed record EdgeRejectedPayload(int From, int To, double Weight, string Reason);

/// <summary>
/// Kruskal's algorithm via union-find. Edges are treated as undirected —
/// a directed pair and its reverse are deduplicated to one candidate edge
/// by canonical (min,max) node order. Deterministic ordering: ascending
/// weight, ties broken by (from,to) — required for rehydration replay.
/// Assumes the input graph may be disconnected: result is a minimum
/// spanning FOREST in that case, not a failure; Value reports whether the
/// result is a single spanning tree (true) or a forest (false).
/// </summary>
public sealed class MinimumSpanningTreeOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } =
        new("minimum-spanning-tree", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        var parent = new int[n];
        var rank = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int x) => parent[x] == x ? x : (parent[x] = Find(parent[x]));
        bool Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return false;
            if (rank[ra] < rank[rb]) (ra, rb) = (rb, ra);
            parent[rb] = ra;
            if (rank[ra] == rank[rb]) rank[ra]++;
            return true;
        }

        var seen = new HashSet<(int, int)>();
        var edges = new List<(int From, int To, double Weight)>();
        foreach (var (from, to, weight) in input.AllDirectedEdges())
        {
            var key = (Math.Min(from, to), Math.Max(from, to));
            if (seen.Add(key)) edges.Add((key.Item1, key.Item2, weight));
        }
        edges.Sort((a, b) =>
        {
            int cmp = a.Weight.CompareTo(b.Weight);
            if (cmp != 0) return cmp;
            cmp = a.From.CompareTo(b.From);
            return cmp != 0 ? cmp : a.To.CompareTo(b.To);
        });

        var trace = new TraceRecorder(context);
        var mstEdges = new List<(int, int, double)>();

        foreach (var (from, to, weight) in edges)
        {
            trace.Add(new DomainStepEvent("EdgeConsidered", $"Considering {from}-{to} ({weight})",
                new EdgeConsideredPayload(from, to, weight)));

            if (Union(from, to))
            {
                mstEdges.Add((from, to, weight));
                trace.Add(new DomainStepEvent("EdgeAdded", $"Added {from}-{to} ({weight})",
                    new EdgeAddedPayload(from, to, weight)));
            }
            else
            {
                trace.Add(new DomainStepEvent("EdgeRejected", $"Rejected {from}-{to}: would form a cycle",
                    new EdgeRejectedPayload(from, to, weight, "cycle")));
            }
        }

        var resultEdges = new List<(int, int, double)>();
        foreach (var (a, b, w) in mstEdges) { resultEdges.Add((a, b, w)); resultEdges.Add((b, a, w)); }

        bool isSpanningTree = Enumerable.Range(0, n).Select(Find).Distinct().Count() == 1;

        return new OperationExecutionResult(true, new Graph(n, resultEdges), isSpanningTree, trace, null);
    }
}