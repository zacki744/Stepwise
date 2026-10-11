using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

/// <summary>
/// Generation: a seeded random graph with exactly `connections` distinct connections between `nodes`
/// nodes (no self-loops, no duplicates). Undirected (default): each connection is stored in both
/// directions. directed: each is a single edge, chosen among all n*(n-1) ordered pairs. maxWeight 1 gives
/// weight 1 everywhere; larger gives integer weights in [1, maxWeight], equal in both directions of an
/// undirected connection. Same seed, same graph. Ignores the input graph.
/// </summary>
public sealed class GenerateRandomGraphOperation : IOperation<Graph>
{
    public const int MaxWeightLimit = 1_000_000;

    private readonly int _nodes;
    private readonly int _connections;
    private readonly int _seed;
    private readonly bool _directed;
    private readonly int _maxWeight;

    public GenerateRandomGraphOperation(int nodes, int connections, int seed, bool directed = false, int maxWeight = 1)
    {
        if (nodes < 0) throw new ArgumentException($"nodes must not be negative (got {nodes})");
        if (connections < 0) throw new ArgumentException($"connections must not be negative (got {connections})");
        if (maxWeight < 1 || maxWeight > MaxWeightLimit)
            throw new ArgumentException($"maxWeight must be between 1 and {MaxWeightLimit:N0} (got {maxWeight})");
        DatasetLimits.EnsureWithinLimit(nodes, $"Generated graph of {nodes:N0} nodes");

        long possible = directed ? (long)nodes * (nodes - 1) : (long)nodes * (nodes - 1) / 2;
        if (connections > possible)
            throw new ArgumentException(
                $"{nodes} nodes allow only {possible:N0} distinct {(directed ? "edges" : "connections")} (asked for {connections:N0})");

        long directedEdges = directed ? connections : 2L * connections;
        if (directedEdges > DatasetLimits.MaxEdges)
            throw new ArgumentException($"That would be {directedEdges:N0} directed edges; the limit is {DatasetLimits.MaxEdges:N0}");

        _nodes = nodes;
        _connections = connections;
        _seed = seed;
        _directed = directed;
        _maxWeight = maxWeight;
    }

    public OperationMetadata Metadata { get; } = new("generate-random-graph", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        var random = new DeterministicRandom(_seed);
        long possible = _directed ? (long)_nodes * (_nodes - 1) : (long)_nodes * (_nodes - 1) / 2;

        // Floyd's algorithm: m distinct values from [0, possible) with no rejection loop.
        var chosen = new HashSet<long>(_connections);
        for (long j = possible - _connections; j < possible; j++)
        {
            context.Checkpoint();
            long t = random.NextInt64(j + 1);
            if (!chosen.Add(t)) chosen.Add(j);
        }
        long[] keys = chosen.ToArray();
        Array.Sort(keys); // hash order is not deterministic; sorted order is

        var trace = new TraceRecorder(context);
        var edges = new List<(int From, int To, double Weight)>(_directed ? keys.Length : 2 * keys.Length);
        foreach (long key in keys)
        {
            var (a, b) = _directed ? UnrankDirected(key, _nodes) : UnrankPair(key, _nodes);
            double weight = _maxWeight == 1 ? 1.0 : random.Next(1, _maxWeight + 1);
            edges.Add((a, b, weight));
            if (!_directed) edges.Add((b, a, weight));
            trace.Add(new DomainStepEvent("ConnectionAdded", $"{a} {(_directed ? "->" : "<->")} {b} (weight {weight})",
                new ConnectionAddedPayload(a, b, weight)));
        }

        return new OperationExecutionResult(true, new Graph(_nodes, edges), null, trace, null);
    }

    // Pairs (a < b) are numbered row by row: all of a=0's, then a=1's, and so on.
    private static long Prefix(long a, long n) => a * (2 * n - a - 1) / 2; // pairs whose first node is below a

    private static (int A, int B) UnrankPair(long index, int n)
    {
        int lo = 0, hi = n - 2;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (Prefix(mid, n) <= index) lo = mid;
            else hi = mid - 1;
        }
        return (lo, lo + 1 + (int)(index - Prefix(lo, n)));
    }

    // Ordered pairs (a != b): from = index / (n-1); the target skips over `from` itself.
    private static (int From, int To) UnrankDirected(long index, int n)
    {
        int from = (int)(index / (n - 1));
        int rest = (int)(index % (n - 1));
        return (from, rest >= from ? rest + 1 : rest);
    }
}