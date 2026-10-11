namespace Engine.Domain.Operations;

internal sealed class FlowNetwork
{
    internal const double Epsilon = 1e-9;

    private readonly int _nodes;
    private readonly int[] _head;
    private readonly int[] _to;
    private readonly int[] _next;
    private readonly double[] _capacity; // residual; edge 2k is forward, 2k+1 its reverse
    private int _used;

    public FlowNetwork(int nodes, int maxEdges)
    {
        _nodes = nodes;
        _head = new int[nodes];
        Array.Fill(_head, -1);
        _to = new int[2 * maxEdges];
        _next = new int[2 * maxEdges];
        _capacity = new double[2 * maxEdges];
    }

    public int EdgeCount => _used / 2;
    public int From(int edge) => _to[2 * edge + 1];
    public int To(int edge) => _to[2 * edge];
    public double Capacity(int edge) => _capacity[2 * edge] + _capacity[2 * edge + 1];
    public double Flow(int edge) => _capacity[2 * edge + 1]; // the reverse edge's residual IS the net flow

    /// <returns>The edge's ordinal (0, 1, 2... in the order edges were added).</returns>
    public int AddEdge(int from, int to, double capacity)
    {
        int forward = _used;
        _to[forward] = to;
        _capacity[forward] = capacity;
        _next[forward] = _head[from];
        _head[from] = forward;

        int back = forward + 1;
        _to[back] = from;
        _capacity[back] = 0;
        _next[back] = _head[to];
        _head[to] = back;

        _used += 2;
        return forward / 2;
    }

    public double MaxFlow(int source, int sink, OperationContext context, Action<double, int, double>? onAugment = null)
    {
        var level = new int[_nodes];
        var current = new int[_nodes];
        var queue = new int[_nodes];
        var path = new int[_nodes];
        double total = 0;

        while (true)
        {
            // Phase: BFS levels over edges that still have capacity.
            Array.Fill(level, -1);
            level[source] = 0;
            int head = 0, tail = 0;
            queue[tail++] = source;
            while (head < tail)
            {
                int u = queue[head++];
                for (int e = _head[u]; e != -1; e = _next[e])
                {
                    context.Checkpoint();
                    if (_capacity[e] > Epsilon && level[_to[e]] < 0)
                    {
                        level[_to[e]] = level[u] + 1;
                        queue[tail++] = _to[e];
                    }
                }
            }
            if (level[sink] < 0) return total;

            // Blocking flow: iterative DFS with a "current arc" per node.
            Array.Copy(_head, current, _nodes);
            int depth = 0, node = source;
            while (true)
            {
                if (node == sink)
                {
                    double bottleneck = double.PositiveInfinity;
                    for (int i = 0; i < depth; i++) bottleneck = Math.Min(bottleneck, _capacity[path[i]]);
                    for (int i = 0; i < depth; i++)
                    {
                        _capacity[path[i]] -= bottleneck;
                        _capacity[path[i] ^ 1] += bottleneck;
                    }
                    total += bottleneck;
                    context.Checkpoint(depth);
                    onAugment?.Invoke(bottleneck, depth, total);
                    depth = 0;
                    node = source;
                    continue;
                }

                bool advanced = false;
                while (current[node] != -1)
                {
                    context.Checkpoint();
                    int e = current[node];
                    if (_capacity[e] > Epsilon && level[_to[e]] == level[node] + 1)
                    {
                        path[depth++] = e;
                        node = _to[e];
                        advanced = true;
                        break;
                    }
                    current[node] = _next[e];
                }
                if (advanced) continue;

                if (node == source) break;        // blocking flow complete: next phase
                level[node] = -1;                 // dead end: never useful again this phase
                int back = path[--depth];
                node = _to[back ^ 1];
                current[node] = _next[current[node]];
            }
        }
    }

    /// <summary>Nodes still reachable from the source through edges with spare capacity (the source side of a minimum cut).</summary>
    public bool[] ResidualReachableFrom(int source)
    {
        var seen = new bool[_nodes];
        var stack = new Stack<int>();
        seen[source] = true;
        stack.Push(source);
        while (stack.Count > 0)
        {
            int u = stack.Pop();
            for (int e = _head[u]; e != -1; e = _next[e])
            {
                if (_capacity[e] <= Epsilon || seen[_to[e]]) continue;
                seen[_to[e]] = true;
                stack.Push(_to[e]);
            }
        }
        return seen;
    }
}