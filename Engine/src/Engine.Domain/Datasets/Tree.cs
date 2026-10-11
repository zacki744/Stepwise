using Engine.Domain.Capabilities;

namespace Engine.Domain.Datasets;

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

    private Tree(List<int?> values, List<int> left, List<int> right, int root)
    {
        _values = values;
        _left = left;
        _right = right;
        _root = root;
    }

    public static Tree Empty() => new([], [], [], -1);

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