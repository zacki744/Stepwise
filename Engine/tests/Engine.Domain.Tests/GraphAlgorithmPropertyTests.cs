using CsCheck;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

/// <summary>
/// Property-based checks: each test draws many random graphs and compares an operation against a
/// deliberately simple reference written independently of the production code (Floyd–Warshall,
/// Prim, Edmonds–Karp, boolean transitive closure). A failure prints the CsCheck seed that
/// reproduces it. Graphs are tiny (2–8 nodes) so the references can be brute force and obviously right.
/// </summary>
public class GraphAlgorithmPropertyTests
{
    private const double Inf = double.PositiveInfinity;

    // ---------- random graph builders (seed in, graph out: deterministic and reproducible) ----------

    private static Graph RandomDirected(DeterministicRandom rnd, int n, int minWeight, int maxWeight, int percentDense)
    {
        var edges = new List<(int From, int To, double Weight)>();
        for (int u = 0; u < n; u++)
            for (int v = 0; v < n; v++)
                if (u != v && rnd.Next(100) < percentDense)
                    edges.Add((u, v, rnd.Next(minWeight, maxWeight + 1)));
        return new Graph(n, edges);
    }

    private static Graph RandomUndirected(DeterministicRandom rnd, int n, int percentDense)
    {
        var edges = new List<(int From, int To, double Weight)>();
        for (int u = 0; u < n; u++)
            for (int v = u + 1; v < n; v++)
                if (rnd.Next(100) < percentDense)
                {
                    double w = rnd.Next(1, 10);
                    edges.Add((u, v, w));
                    edges.Add((v, u, w));
                }
        return new Graph(n, edges);
    }

    // ---------- references ----------

    private static double[,] FloydWarshall(Graph g)
    {
        int n = g.NodeCount;
        var d = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                d[i, j] = i == j ? 0 : Inf;
        foreach (var (u, v, w) in g.AllDirectedEdges())
            d[u, v] = Math.Min(d[u, v], w);

        for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (!double.IsPositiveInfinity(d[i, k]) && !double.IsPositiveInfinity(d[k, j]) && d[i, k] + d[k, j] < d[i, j])
                        d[i, j] = d[i, k] + d[k, j];
        return d;
    }

    private static bool[,] Reachability(Graph g)
    {
        int n = g.NodeCount;
        var r = new bool[n, n];
        for (int i = 0; i < n; i++) r[i, i] = true;
        foreach (var (u, v, _) in g.AllDirectedEdges()) r[u, v] = true;
        for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (r[i, k] && r[k, j]) r[i, j] = true;
        return r;
    }

    /// <summary>Total weight of a minimum spanning forest, by Prim's algorithm from every unvisited node.</summary>
    private static (double Weight, int Components) PrimForest(Graph g)
    {
        int n = g.NodeCount;
        var w = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                w[i, j] = Inf;
        foreach (var (u, v, weight) in g.AllDirectedEdges())
        {
            w[u, v] = Math.Min(w[u, v], weight);
            w[v, u] = Math.Min(w[v, u], weight);
        }

        var inTree = new bool[n];
        double total = 0;
        int components = 0;
        for (int root = 0; root < n; root++)
        {
            if (inTree[root]) continue;
            components++;
            var best = new double[n];
            Array.Fill(best, Inf);
            best[root] = 0;
            while (true)
            {
                int pick = -1;
                for (int v = 0; v < n; v++)
                    if (!inTree[v] && !double.IsPositiveInfinity(best[v]) && (pick == -1 || best[v] < best[pick])) pick = v;
                if (pick == -1) break;
                inTree[pick] = true;
                total += best[pick];
                for (int v = 0; v < n; v++)
                    if (!inTree[v] && w[pick, v] < best[v]) best[v] = w[pick, v];
            }
        }
        return (total, components);
    }

    /// <summary>Edmonds–Karp on a capacity matrix.</summary>
    private static double ReferenceMaxFlow(Graph g, int source, int sink)
    {
        int n = g.NodeCount;
        var cap = new double[n, n];
        foreach (var (u, v, c) in g.AllDirectedEdges()) cap[u, v] += c;

        double flow = 0;
        while (true)
        {
            var parent = new int[n];
            Array.Fill(parent, -1);
            parent[source] = source;
            var queue = new Queue<int>();
            queue.Enqueue(source);
            while (queue.Count > 0 && parent[sink] == -1)
            {
                int u = queue.Dequeue();
                for (int v = 0; v < n; v++)
                    if (parent[v] == -1 && cap[u, v] > 1e-12) { parent[v] = u; queue.Enqueue(v); }
            }
            if (parent[sink] == -1) return flow;

            double bottleneck = Inf;
            for (int v = sink; v != source; v = parent[v]) bottleneck = Math.Min(bottleneck, cap[parent[v], v]);
            for (int v = sink; v != source; v = parent[v]) { cap[parent[v], v] -= bottleneck; cap[v, parent[v]] += bottleneck; }
            flow += bottleneck;
        }
    }

    // ---------- properties ----------

    [Fact]
    public void ShortestPath_MatchesFloydWarshall_AndReturnsAValidPath()
    {
        Gen.Int.Sample(seed =>
        {
            var rnd = new DeterministicRandom(seed);
            int n = rnd.Next(2, 9);
            var graph = RandomDirected(rnd, n, minWeight: 0, maxWeight: 9, percentDense: 35);
            var reference = FloydWarshall(graph);

            for (int s = 0; s < n; s++)
                for (int t = 0; t < n; t++)
                {
                    var result = new ShortestPathOperation(s, t).Execute(graph);

                    if (double.IsPositiveInfinity(reference[s, t]))
                    {
                        Assert.Null(result.Value);
                        continue;
                    }

                    var found = Assert.IsType<ShortestPathResult>(result.Value);
                    Assert.Equal(reference[s, t], found.TotalDistance, 9);
                    Assert.Equal(s, found.Path[0]);
                    Assert.Equal(t, found.Path[^1]);

                    double walked = 0;
                    for (int i = 0; i + 1 < found.Path.Count; i++)
                        walked += graph.GetWeight(found.Path[i], found.Path[i + 1]); // throws if the edge doesn't exist
                    Assert.Equal(found.TotalDistance, walked, 9);
                }
        });
    }

    [Fact]
    public void BellmanFord_MatchesFloydWarshall_IncludingNegativeCycles()
    {
        Gen.Int.Sample(seed =>
        {
            var rnd = new DeterministicRandom(seed);
            int n = rnd.Next(2, 9);
            var graph = RandomDirected(rnd, n, minWeight: -3, maxWeight: 9, percentDense: 30);
            var reference = FloydWarshall(graph);

            for (int s = 0; s < n; s++)
            {
                // A negative cycle matters if some node reachable from s lies on one.
                bool negativeCycleReachable = Enumerable.Range(0, n)
                    .Any(v => !double.IsPositiveInfinity(reference[s, v]) && reference[v, v] < 0);

                for (int t = 0; t < n; t++)
                {
                    var result = (BellmanFordResult)new BellmanFordOperation(s, t).Execute(graph).Value!;

                    if (negativeCycleReachable)
                    {
                        Assert.True(result.NegativeCycle);
                        Assert.False(result.Found);
                    }
                    else if (double.IsPositiveInfinity(reference[s, t]))
                    {
                        Assert.False(result.NegativeCycle);
                        Assert.False(result.Found);
                    }
                    else
                    {
                        Assert.False(result.NegativeCycle);
                        Assert.True(result.Found);
                        Assert.Equal(reference[s, t], result.Distance!.Value, 9);
                    }
                }
            }
        });
    }

    [Fact]
    public void MinimumSpanningTree_HasTheSameWeightAsPrim_AndSpansEveryComponent()
    {
        Gen.Int.Sample(seed =>
        {
            var rnd = new DeterministicRandom(seed);
            int n = rnd.Next(2, 9);
            var graph = RandomUndirected(rnd, n, percentDense: 40);
            var (expectedWeight, components) = PrimForest(graph);

            var result = new MinimumSpanningTreeOperation().Execute(graph);
            var forest = (Graph)result.NewState!;

            // Each tree edge is stored in both directions.
            double weight = forest.AllDirectedEdges().Sum(e => e.Weight) / 2;
            Assert.Equal(expectedWeight, weight, 9);
            Assert.Equal(n - components, forest.EdgeCount / 2);
            Assert.Equal(components == 1, (bool)result.Value!);
        });
    }

    [Fact]
    public void MaxFlow_MatchesEdmondsKarp_AndEqualsTheMinCut_AndConservesFlow()
    {
        Gen.Int.Sample(seed =>
        {
            var rnd = new DeterministicRandom(seed);
            int n = rnd.Next(2, 9);
            var graph = RandomDirected(rnd, n, minWeight: 0, maxWeight: 9, percentDense: 40);
            int source = 0, sink = n - 1;

            var result = (MaxFlowResult)new MaxFlowOperation(source, sink).Execute(graph).Value!;

            Assert.Equal(ReferenceMaxFlow(graph, source, sink), result.Value, 9);
            Assert.Equal(result.Value, result.MinCut.Sum(e => e.Capacity), 9); // max-flow min-cut theorem

            var net = new double[n];
            foreach (var edge in result.Flows)
            {
                Assert.InRange(edge.Flow, 0, edge.Capacity + 1e-9);
                net[edge.From] -= edge.Flow;
                net[edge.To] += edge.Flow;
            }
            for (int v = 0; v < n; v++)
            {
                if (v == source) Assert.Equal(-result.Value, net[v], 9);
                else if (v == sink) Assert.Equal(result.Value, net[v], 9);
                else Assert.Equal(0, net[v], 9);
            }
        });
    }

    [Fact]
    public void StronglyConnectedComponents_MatchMutualReachability()
    {
        Gen.Int.Sample(seed =>
        {
            var rnd = new DeterministicRandom(seed);
            int n = rnd.Next(2, 9);
            var graph = RandomDirected(rnd, n, minWeight: 1, maxWeight: 1, percentDense: 25);
            var reach = Reachability(graph);

            var result = (SccResult)new StronglyConnectedComponentsOperation().Execute(graph).Value!;

            var componentOf = new int[n];
            Array.Fill(componentOf, -1);
            for (int c = 0; c < result.Components.Count; c++)
                foreach (int node in result.Components[c])
                {
                    Assert.Equal(-1, componentOf[node]); // each node is listed exactly once
                    componentOf[node] = c;
                }
            Assert.DoesNotContain(-1, componentOf);

            for (int u = 0; u < n; u++)
                for (int v = 0; v < n; v++)
                    Assert.Equal(reach[u, v] && reach[v, u], componentOf[u] == componentOf[v]);

            Assert.Equal(result.Components.Count, result.ComponentCount);
        });
    }
}
