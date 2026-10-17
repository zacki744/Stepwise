using Engine.Domain.Capabilities;

namespace Engine.Domain.Datasets;

/// <summary>One live node of a tree: its slot, value, and child slots (-1 for none). The shape snapshots use.</summary>
public sealed record TreeNodeData(int Slot, int Value, int Left, int Right);

/// <summary>
/// Binary search tree. Backed by parallel lists, not Node objects — children
/// referenced by index, -1 meaning "no child" (same representation style as
/// Graph's CSR arrays). Append-only: a removed slot is simply unreferenced,
/// not reclaimed. Fine at demo scale; worth revisiting with a free-list if a
/// real workload ever shows it mattering — not before.
/// </summary>
public sealed class Tree : IDataset, ITraversable<int>, IOrderable<int>
{
    private readonly List<int?> _values;
    private readonly List<int> _left;
    private readonly List<int> _right;
    private readonly int _root; // -1 means empty tree
    public int SlotCount => _values.Count;
    public long EstimatedBytes => 64L + 16L * _values.Count; // value (int?, 8) + left (4) + right (4) per slot

    private Tree(List<int?> values, List<int> left, List<int> right, int root)
    {
        _values = values;
        _left = left;
        _right = right;
        _root = root;
    }

    public static Tree Empty() => new([], [], [], -1);

    /// <summary>Most child-pointer steps <see cref="FromValues"/> will take. Sorted input degenerates to a chain and costs O(n^2).</summary>
    public const long MaxBuildSteps = 50_000_000;

    /// <summary>
    /// Builds a tree by inserting the values one after another (left &lt; node &lt;= right), exactly as
    /// repeated insert-node would. Rejects inputs whose insertions would take unreasonably long.
    /// </summary>
    public static Tree FromValues(IEnumerable<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var v = new List<int?>(); var l = new List<int>(); var r = new List<int>();
        int root = -1;
        long steps = 0;

        foreach (int value in values)
        {
            DatasetLimits.EnsureWithinLimit(v.Count + 1L, "Tree");
            int slot = v.Count;
            v.Add(value); l.Add(-1); r.Add(-1);
            if (root == -1) { root = slot; continue; }

            int current = root;
            while (true)
            {
                if (++steps > MaxBuildSteps)
                    throw new ArgumentException(
                        "Inserting these values one by one would take too long (a long sorted run makes the tree a chain); " +
                        "send the tree as nodes instead, or shuffle the values");
                var side = value < v[current]!.Value ? l : r;
                if (side[current] == -1) { side[current] = slot; break; }
                current = side[current];
            }
        }
        return FromArrays(v, l, r, root);
    }

    /// <summary>The nodes reachable from the root, in slot order. Dead slots (removed nodes) are not included.</summary>
    public IReadOnlyList<TreeNodeData> LiveNodes()
    {
        var result = new List<TreeNodeData>();
        if (_root == -1) return result;

        var stack = new Stack<int>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            int slot = stack.Pop();
            result.Add(new TreeNodeData(slot, _values[slot]!.Value, _left[slot], _right[slot]));
            if (_left[slot] != -1) stack.Push(_left[slot]);
            if (_right[slot] != -1) stack.Push(_right[slot]);
        }
        result.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        return result;
    }

    /// <summary>
    /// Rebuilds a tree from its live nodes, keeping every node in its slot (so later operations that
    /// mention slots behave exactly as they would have on the original). Validates everything a client
    /// could get wrong: duplicate or out-of-range slots, a missing root or child, a node with two
    /// parents, a cycle, an unreachable node, or a violation of left &lt; node &lt;= right.
    /// <paramref name="slotCount"/> restores trailing dead slots; it defaults to the highest slot + 1.
    /// </summary>
    public static Tree FromNodes(int root, IReadOnlyList<TreeNodeData> nodes, int? slotCount = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        int maxSlot = -1;
        foreach (var n in nodes) maxSlot = Math.Max(maxSlot, n.Slot);
        int count = slotCount ?? maxSlot + 1;
        if (count < 0 || count <= maxSlot) throw new ArgumentException("slotCount is smaller than the highest slot");
        DatasetLimits.EnsureWithinLimit(count, "Tree");

        var values = new List<int?>(new int?[count]);
        var left = new List<int>(Enumerable.Repeat(-1, count));
        var right = new List<int>(Enumerable.Repeat(-1, count));
        var present = new bool[count];

        foreach (var n in nodes)
        {
            if (n.Slot < 0) throw new ArgumentException($"Invalid slot {n.Slot}");
            if (present[n.Slot]) throw new ArgumentException($"Duplicate slot {n.Slot}");
            present[n.Slot] = true;
            values[n.Slot] = n.Value;
            left[n.Slot] = n.Left;
            right[n.Slot] = n.Right;
        }

        if (nodes.Count == 0)
        {
            if (root != -1) throw new ArgumentException("An empty tree cannot have a root");
            return FromArrays(values, left, right, -1);
        }
        if (root < 0 || root >= count || !present[root]) throw new ArgumentException("The root slot does not exist");

        var hasParent = new bool[count];
        foreach (var n in nodes)
            foreach (int child in new[] { n.Left, n.Right })
            {
                if (child == -1) continue;
                if (child < 0 || child >= count || !present[child])
                    throw new ArgumentException($"Child {child} of slot {n.Slot} does not exist");
                if (child == root || hasParent[child])
                    throw new ArgumentException($"Slot {child} has more than one parent");
                hasParent[child] = true;
            }

        // left subtree < node <= right subtree. lo is inclusive, hi exclusive.
        var pending = new Stack<(int Slot, long Lo, long Hi)>();
        pending.Push((root, long.MinValue, long.MaxValue));
        int visited = 0;
        while (pending.Count > 0)
        {
            var (slot, lo, hi) = pending.Pop();
            if (++visited > nodes.Count) throw new ArgumentException("The tree contains a cycle");
            long value = values[slot]!.Value;
            if (value < lo || value >= hi) throw new ArgumentException($"Slot {slot} breaks the search-tree order");
            if (left[slot] != -1) pending.Push((left[slot], lo, value));
            if (right[slot] != -1) pending.Push((right[slot], value, hi));
        }
        if (visited != nodes.Count) throw new ArgumentException("Some nodes are not reachable from the root");

        return FromArrays(values, left, right, root);
    }

    public int? RootIndex => _root == -1 ? null : _root;
    public int GetValue(int index) => _values[index]!.Value;
    public int? LeftChild(int index) => _left[index] == -1 ? null : _left[index];
    public int? RightChild(int index) => _right[index] == -1 ? null : _right[index];

    public IEnumerable<int> GetNeighbors(int node)
    {
        if (_left[node] != -1) yield return _left[node];
        if (_right[node] != -1) yield return _right[node];
    }

    public IEnumerable<int> InOrder()
    {
        if (_root == -1) yield break;

        var stack = new Stack<int>();
        int current = _root;
        while (current != -1 || stack.Count > 0)
        {
            while (current != -1)
            {
                stack.Push(current);
                current = _left[current];
            }
            current = stack.Pop();
            yield return current;
            current = _right[current];
        }
    }

    internal static Tree FromArrays(List<int?> values, List<int> left, List<int> right, int root)
    {
        DatasetLimits.EnsureWithinLimit(values.Count, "Tree");
        return new(values, left, right, root);
    }

    internal List<int?> CloneValues() => [.. _values];
    internal List<int> CloneLeft() => [.. _left];
    internal List<int> CloneRight() => [.. _right];
}