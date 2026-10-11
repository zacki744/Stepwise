using Engine.Domain.Datasets;

namespace Engine.Domain.Operations;

/// <summary>
/// Tarjan's low-link DFS with an explicit stack, so depth is limited by heap, not call stack.
/// Assumes an undirected graph (each edge stored in both directions). The tree edge back to a
/// node's parent is skipped exactly once, so a genuinely duplicated edge counts as a back-edge
/// (and is correctly not a bridge). Deterministic: roots ascend, neighbors follow CSR order.
/// </summary>
internal sealed class LowLinkAnalysis
{
    public bool[] IsArticulation { get; }
    public List<(int Node, int Time)> VisitOrder { get; } = [];
    public List<(int A, int B)> Bridges { get; } = [];

    public LowLinkAnalysis(Graph graph)
    {
        int n = graph.NodeCount;
        IsArticulation = new bool[n];

        var disc = new int[n];                // 0 = unvisited; discovery times start at 1
        var low = new int[n];
        var parent = new int[n];
        Array.Fill(parent, -1);
        var cursor = new int[n];              // next neighbor index to examine, per node
        var children = new int[n];
        var parentEdgeSkipped = new bool[n];
        var stack = new Stack<int>();
        int timer = 0;

        for (int root = 0; root < n; root++)
        {
            if (disc[root] != 0) continue;

            disc[root] = low[root] = ++timer;
            VisitOrder.Add((root, disc[root]));
            stack.Push(root);

            while (stack.Count > 0)
            {
                int u = stack.Peek();

                if (cursor[u] < graph.Degree(u))
                {
                    int v = graph.NeighborAt(u, cursor[u]++);
                    if (disc[v] == 0)
                    {
                        parent[v] = u;
                        children[u]++;
                        disc[v] = low[v] = ++timer;
                        VisitOrder.Add((v, disc[v]));
                        stack.Push(v);
                    }
                    else if (v == parent[u] && !parentEdgeSkipped[u])
                    {
                        parentEdgeSkipped[u] = true; // the tree edge itself
                    }
                    else
                    {
                        low[u] = Math.Min(low[u], disc[v]);
                    }
                }
                else
                {
                    stack.Pop();
                    int p = parent[u];
                    if (p != -1)
                    {
                        low[p] = Math.Min(low[p], low[u]);
                        if (low[u] > disc[p]) Bridges.Add((Math.Min(p, u), Math.Max(p, u)));
                        if (parent[p] != -1 && low[u] >= disc[p]) IsArticulation[p] = true;
                    }
                    else if (children[u] > 1)
                    {
                        IsArticulation[u] = true; // a root is a cut vertex only with 2+ DFS children
                    }
                }
            }
        }
    }
}