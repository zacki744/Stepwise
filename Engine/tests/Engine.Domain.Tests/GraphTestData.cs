using Engine.Domain.Datasets;

namespace Engine.Domain.Tests;

internal static class GraphTestData
{
    /// <summary>Each pair is stored as two directed edges, the way add-connection and generate-maze store connections.</summary>
    public static Graph Undirected(int nodeCount, params (int A, int B)[] connections)
    {
        var edges = new List<(int From, int To, double Weight)>();
        foreach (var (a, b) in connections)
        {
            edges.Add((a, b, 1.0));
            edges.Add((b, a, 1.0));
        }
        return new Graph(nodeCount, edges);
    }

    /// <summary>Edges exactly as given, one direction each, weight 1.</summary>
    public static Graph Directed(int nodeCount, params (int From, int To)[] edges) => new(nodeCount, edges);

    public static Graph DirectedChain(int nodeCount) =>
        new(nodeCount, Enumerable.Range(0, nodeCount - 1).Select(i => (From: i, To: i + 1)));

    public static List<(int From, int To, double Weight)> Edges(Graph graph) =>
        graph.AllDirectedEdges().OrderBy(e => e.From).ThenBy(e => e.To).ToList();
}