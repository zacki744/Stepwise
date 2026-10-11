using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record RelaxationRoundPayload(int Round, int Relaxed);
public sealed record BellmanFordResult(
    bool Found, bool NegativeCycle, double? Distance, IReadOnlyList<int>? Path, bool PathOmitted);

public sealed record PivotProcessedPayload(int Pivot, long Improved);
public sealed record AllPairsResult(
    int NodeCount, bool NegativeCycle, long ReachablePairs, double? Diameter, int? DiameterFrom, int? DiameterTo,
    IReadOnlyList<IReadOnlyList<double?>>? Distances);

public enum CentralityMeasure { Degree, Closeness, Betweenness }
public sealed record SourceProcessedPayload(int Source, int Reached);
public sealed record NodeScore(int Node, double Score);
public sealed record CentralityResult(string Measure, bool Directed, IReadOnlyList<NodeScore> Top);

/// <summary>
/// Single-source shortest path that allows negative weights. If a negative cycle is reachable from
/// `start`, shortest paths are undefined and the result says so (NegativeCycle = true, Found = false),
/// even if `target` itself happens to be unaffected. Unreachable target: Found = false.
/// Cost depends on edge order: it stops as soon as a full pass changes nothing, but a worst-case graph
/// needs V-1 passes, which the work budget bounds.
/// </summary>
public sealed class BellmanFordOperation(int start, int target) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("bellman-ford", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        input.EnsureNode(start, nameof(start));
        input.EnsureNode(target, nameof(target));
        int n = input.NodeCount;

        var distance = new double[n];
        Array.Fill(distance, double.PositiveInfinity);
        distance[start] = 0;
        var predecessor = new int[n];
        Array.Fill(predecessor, -1);
        var trace = new TraceRecorder(context);

        bool changed = true;
        for (int round = 1; round <= n - 1 && changed; round++)
        {
            context.Checkpoint(n + (long)input.EdgeCount);
            changed = false;
            int relaxed = 0;
            for (int u = 0; u < n; u++)
            {
                if (double.IsPositiveInfinity(distance[u])) continue;
                for (int i = 0; i < input.Degree(u); i++)
                {
                    int v = input.NeighborAt(u, i);
                    double candidate = distance[u] + input.WeightAt(u, i);
                    if (candidate >= distance[v]) continue;
                    distance[v] = candidate;
                    predecessor[v] = u;
                    changed = true;
                    relaxed++;
                }
            }
            trace.Add(new DomainStepEvent("RelaxationRound", $"Round {round}: {relaxed} distance(s) improved",
                new RelaxationRoundPayload(round, relaxed)));
        }

        // If the last round still improved something, one more pass decides whether a negative cycle is why.
        if (changed && CanStillRelax(input, distance))
        {
            trace.Add(new DomainStepEvent("NegativeCycle", $"A negative cycle is reachable from {start}"));
            return new OperationExecutionResult(true, null, new BellmanFordResult(false, true, null, null, false), trace, null);
        }

        if (double.IsPositiveInfinity(distance[target]))
        {
            trace.Add(new DomainStepEvent("NoPath", $"{target} is not reachable from {start}"));
            return new OperationExecutionResult(true, null, new BellmanFordResult(false, false, null, null, false), trace, null);
        }

        List<int>? path = null;
        for (int at = target, steps = 0; at != -1 && steps <= n; at = predecessor[at], steps++)
        {
            path ??= [];
            path.Add(at);
        }
        path!.Reverse();
        bool omitted = path.Count > DatasetLimits.MaxReturnedItems;

        trace.Add(new DomainStepEvent("PathFound", $"Distance {distance[target]}, {path.Count} node(s)"));
        return new OperationExecutionResult(true, null,
            new BellmanFordResult(true, false, distance[target], omitted ? null : path, omitted), trace, null);
    }

    private static bool CanStillRelax(Graph graph, double[] distance)
    {
        for (int u = 0; u < graph.NodeCount; u++)
        {
            if (double.IsPositiveInfinity(distance[u])) continue;
            for (int i = 0; i < graph.Degree(u); i++)
                if (distance[u] + graph.WeightAt(u, i) < distance[graph.NeighborAt(u, i)]) return true;
        }
        return false;
    }
}

/// <summary>
/// Floyd-Warshall: shortest distance between every ordered pair, following edges as stored (parallel
/// edges: the cheapest wins). Limited to MaxNodes because it is cubic. Distances (null = unreachable) is
/// included only up to MaxNodesWithMatrix nodes; beyond that, the summary fields still describe the result.
/// Diameter is the largest finite distance between two different nodes. A negative cycle (including a
/// negative self-loop) makes distances meaningless: NegativeCycle = true and the rest is omitted.
/// </summary>
public sealed class AllPairsShortestPathsOperation : IOperation<Graph>
{
    public const int MaxNodes = 500;
    public const int MaxNodesWithMatrix = 100;

    public OperationMetadata Metadata { get; } = new("all-pairs-shortest-paths", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        if (n > MaxNodes)
            throw new ArgumentException(
                $"all-pairs-shortest-paths supports at most {MaxNodes} nodes (this graph has {n}); use shortest-path or bellman-ford from a single source instead");

        var d = new double[n * n];
        Array.Fill(d, double.PositiveInfinity);
        for (int i = 0; i < n; i++) d[i * n + i] = 0;
        for (int u = 0; u < n; u++)
        {
            for (int i = 0; i < input.Degree(u); i++)
            {
                int slot = u * n + input.NeighborAt(u, i);
                d[slot] = Math.Min(d[slot], input.WeightAt(u, i));
            }
        }

        var trace = new TraceRecorder(context);
        for (int k = 0; k < n; k++)
        {
            context.Checkpoint((long)n * n);
            long improved = 0;
            for (int i = 0; i < n; i++)
            {
                double viaK = d[i * n + k];
                if (double.IsPositiveInfinity(viaK)) continue;
                for (int j = 0; j < n; j++)
                {
                    double fromK = d[k * n + j];
                    if (double.IsPositiveInfinity(fromK)) continue;
                    double candidate = viaK + fromK;
                    if (candidate >= d[i * n + j]) continue;
                    d[i * n + j] = candidate;
                    improved++;
                }
            }
            trace.Add(new DomainStepEvent("PivotProcessed", $"Allowing paths through node {k}: {improved} pair(s) improved",
                new PivotProcessedPayload(k, improved)));
        }

        for (int i = 0; i < n; i++)
        {
            if (d[i * n + i] >= 0) continue;
            trace.Add(new DomainStepEvent("NegativeCycle", $"Node {i} lies on a negative cycle"));
            return new OperationExecutionResult(true, null, new AllPairsResult(n, true, 0, null, null, null, null), trace, null);
        }

        long reachable = 0;
        double diameter = 0;
        int? diameterFrom = null, diameterTo = null;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i == j || double.IsPositiveInfinity(d[i * n + j])) continue;
                reachable++;
                if (diameterFrom is null || d[i * n + j] > diameter)
                {
                    diameter = d[i * n + j];
                    diameterFrom = i;
                    diameterTo = j;
                }
            }
        }

        List<IReadOnlyList<double?>>? rows = null;
        if (n <= MaxNodesWithMatrix)
        {
            rows = new List<IReadOnlyList<double?>>(n);
            for (int i = 0; i < n; i++)
            {
                var row = new double?[n];
                for (int j = 0; j < n; j++)
                    row[j] = double.IsPositiveInfinity(d[i * n + j]) ? null : d[i * n + j];
                rows.Add(row);
            }
        }

        var result = new AllPairsResult(n, false, reachable, diameterFrom is null ? null : diameter, diameterFrom, diameterTo, rows);
        return new OperationExecutionResult(true, null, result, trace, null);
    }
}

/// <summary>
/// Ranks nodes by degree (out-degree), closeness or betweenness and returns the top k (ties go to the
/// lowest node index). Paths are hop counts, following edges as stored.
///   closeness: (reached / (n-1)) * (reached / sum of distances), so nodes that reach little score low.
///   betweenness: Brandes' algorithm. With directed = false (default) the graph is assumed to store each
///   connection in both directions and scores are halved so each pair counts once; with directed = true
///   they are raw ordered-pair counts.
/// Closeness and betweenness run a search from every node, so they are limited to MaxNodes.
/// </summary>
public sealed class CentralityOperation : IOperation<Graph>
{
    public const int MaxNodes = 2_000;
    public const int MaxTop = 1_000;

    private readonly CentralityMeasure _measure;
    private readonly int _k;
    private readonly bool _directed;

    public CentralityOperation(string measure = "betweenness", int k = 10, bool directed = false)
    {
        ArgumentNullException.ThrowIfNull(measure);
        _measure = measure.ToLowerInvariant() switch
        {
            "degree" => CentralityMeasure.Degree,
            "closeness" => CentralityMeasure.Closeness,
            "betweenness" => CentralityMeasure.Betweenness,
            _ => throw new ArgumentException($"Unknown measure '{measure}'; expected degree, closeness or betweenness")
        };
        if (k < 1 || k > MaxTop) throw new ArgumentException($"k must be between 1 and {MaxTop} (got {k})");
        _k = k;
        _directed = directed;
    }

    public OperationMetadata Metadata { get; } = new("centrality", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        if (_measure != CentralityMeasure.Degree && n > MaxNodes)
            throw new ArgumentException(
                $"{_measure.ToString().ToLowerInvariant()} centrality supports at most {MaxNodes} nodes (this graph has {n}); use degree, or take a subgraph first");

        var trace = new TraceRecorder(context);
        double[] scores = _measure switch
        {
            CentralityMeasure.Degree => DegreeScores(input),
            CentralityMeasure.Closeness => ClosenessScores(input, context, trace),
            _ => BetweennessScores(input, context, trace)
        };

        var top = Enumerable.Range(0, n)
            .OrderByDescending(node => scores[node]).ThenBy(node => node)
            .Take(_k).Select(node => new NodeScore(node, scores[node])).ToList();

        var result = new CentralityResult(_measure.ToString().ToLowerInvariant(), _directed, top);
        return new OperationExecutionResult(true, null, result, trace, null);
    }

    private static double[] DegreeScores(Graph graph)
    {
        var scores = new double[graph.NodeCount];
        for (int node = 0; node < graph.NodeCount; node++) scores[node] = graph.Degree(node);
        return scores;
    }

    private static double[] ClosenessScores(Graph graph, OperationContext context, TraceRecorder trace)
    {
        int n = graph.NodeCount;
        var scores = new double[n];
        var dist = new int[n];
        var queue = new int[n];

        for (int s = 0; s < n; s++)
        {
            context.Checkpoint(n + (long)graph.EdgeCount);
            Array.Fill(dist, -1);
            dist[s] = 0;
            int head = 0, tail = 0;
            queue[tail++] = s;
            long total = 0;
            while (head < tail)
            {
                int u = queue[head++];
                for (int i = 0; i < graph.Degree(u); i++)
                {
                    int v = graph.NeighborAt(u, i);
                    if (dist[v] >= 0) continue;
                    dist[v] = dist[u] + 1;
                    total += dist[v];
                    queue[tail++] = v;
                }
            }

            int reached = tail - 1;
            scores[s] = reached == 0 ? 0 : (reached / (double)(n - 1)) * (reached / (double)total);
            trace.Add(new DomainStepEvent("SourceProcessed", $"Node {s} reaches {reached} node(s)",
                new SourceProcessedPayload(s, reached)));
        }
        return scores;
    }

    private double[] BetweennessScores(Graph graph, OperationContext context, TraceRecorder trace)
    {
        int n = graph.NodeCount;
        var scores = new double[n];
        var dist = new int[n];
        var sigma = new double[n];
        var delta = new double[n];
        var order = new int[n];

        for (int s = 0; s < n; s++)
        {
            context.Checkpoint(n + (long)graph.EdgeCount);
            Array.Fill(dist, -1);
            Array.Clear(sigma);
            Array.Clear(delta);
            int head = 0, tail = 0;
            dist[s] = 0;
            sigma[s] = 1;
            order[tail++] = s;

            while (head < tail)
            {
                int u = order[head++];
                for (int i = 0; i < graph.Degree(u); i++)
                {
                    int v = graph.NeighborAt(u, i);
                    if (dist[v] < 0) { dist[v] = dist[u] + 1; order[tail++] = v; }
                    if (dist[v] == dist[u] + 1) sigma[v] += sigma[u];
                }
            }

            // Walk back from the farthest node: each node collects credit from the nodes just beyond it.
            for (int idx = tail - 1; idx >= 0; idx--)
            {
                int u = order[idx];
                for (int i = 0; i < graph.Degree(u); i++)
                {
                    int v = graph.NeighborAt(u, i);
                    if (dist[v] == dist[u] + 1) delta[u] += sigma[u] / sigma[v] * (1 + delta[v]);
                }
                if (u != s) scores[u] += delta[u];
            }

            trace.Add(new DomainStepEvent("SourceProcessed", $"Node {s} reaches {tail - 1} node(s)",
                new SourceProcessedPayload(s, tail - 1)));
        }

        if (!_directed)
            for (int node = 0; node < n; node++) scores[node] /= 2;
        return scores;
    }
}