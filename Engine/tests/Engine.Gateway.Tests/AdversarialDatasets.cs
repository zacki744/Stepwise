using Engine.Domain;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;

namespace Engine.Gateway.Tests;

/// <summary>A named dataset factory. Datasets are immutable, but each test still asks for a fresh one.</summary>
public sealed record NamedDataset(string Name, Func<IDataset> Create);

/// <summary>
/// Datasets chosen to break assumptions: empty and single-element shapes, degenerate structures, extreme
/// values. Every operation is run against every dataset of its type (see EdgeCaseMatrixTests).
/// </summary>
public static class AdversarialDatasets
{
    public static IReadOnlyList<NamedDataset> For(string datasetType) => datasetType switch
    {
        "matrix" => Matrices,
        "graph" => Graphs,
        "tree" => Trees,
        _ => throw new ArgumentException($"No adversarial datasets for '{datasetType}'")
    };

    // ---------- matrix ----------

    private static Matrix Grid(int rows, int columns, Func<int, int, int> cell)
    {
        var matrix = new Matrix(rows, columns);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                matrix[r, c] = cell(r, c);
        return matrix;
    }

    public static readonly IReadOnlyList<NamedDataset> Matrices =
    [
        new("1x1 open", () => Grid(1, 1, (_, _) => 0)),
        new("1x1 wall", () => Grid(1, 1, (_, _) => 5)),
        new("one row of 300", () => Grid(1, 300, (_, c) => c % 3)),
        new("one column of 300", () => Grid(300, 1, (r, _) => r % 3)),
        new("20x20 all open", () => Grid(20, 20, (_, _) => 0)),
        new("20x20 all walls", () => Grid(20, 20, (_, _) => 7)),
        new("9x9 checkerboard", () => Grid(9, 9, (r, c) => (r + c) % 2)),
        new("extreme values", () => Grid(4, 4, (r, c) => (r + c) % 2 == 0 ? int.MaxValue : int.MinValue)),
        new("negative values", () => Grid(5, 5, (r, c) => -(r * 5 + c) - 1)),
        new("walled in start", () => Grid(6, 6, (r, c) => r == 0 && c == 0 ? 0 : (r <= 1 && c <= 1 ? 1 : 0))),
    ];

    // ---------- graph ----------

    private static Graph Directed(int nodes, params (int From, int To, double Weight)[] edges) => new(nodes, edges);

    private static Graph Undirected(int nodes, params (int From, int To, double Weight)[] edges) =>
        new(nodes, edges.SelectMany(e => e.From == e.To
            ? new[] { e }
            : new[] { e, (e.To, e.From, e.Weight) }).ToArray());

    private static Graph Complete(int nodes) =>
        Undirected(nodes, Enumerable.Range(0, nodes).SelectMany(a =>
            Enumerable.Range(a + 1, nodes - a - 1).Select(b => (a, b, 1.0))).ToArray());

    public static readonly IReadOnlyList<NamedDataset> Graphs =
    [
        new("no nodes", () => Graph.Empty(0)),
        new("one node", () => Graph.Empty(1)),
        new("one node with a self-loop", () => Directed(1, (0, 0, 1))),
        new("two nodes, no edges", () => Graph.Empty(2)),
        new("one directed edge", () => Directed(2, (0, 1, 1))),
        new("undirected path of 6", () => Undirected(6, (0, 1, 1), (1, 2, 1), (2, 3, 1), (3, 4, 1), (4, 5, 1))),
        new("directed cycle of 5", () => Directed(5, (0, 1, 1), (1, 2, 1), (2, 3, 1), (3, 4, 1), (4, 0, 1))),
        new("directed diamond (DAG)", () => Directed(4, (0, 1, 1), (0, 2, 1), (1, 3, 1), (2, 3, 1))),
        new("star of 8", () => Undirected(8, Enumerable.Range(1, 7).Select(i => (0, i, 1.0)).ToArray())),
        new("complete graph of 6", () => Complete(6)),
        new("two components", () => Undirected(6, (0, 1, 1), (1, 2, 1), (3, 4, 1), (4, 5, 1))),
        new("self-loops everywhere", () => Directed(4, (0, 0, 1), (1, 1, 1), (2, 2, 1), (3, 3, 1), (0, 1, 1), (1, 2, 1))),
        new("parallel edges", () => Directed(3, (0, 1, 1), (0, 1, 2), (0, 1, 3), (1, 2, 1))),
        new("negative weights", () => Directed(4, (0, 1, -2), (1, 2, -3), (2, 3, 4), (0, 3, 1))),
        new("negative cycle", () => Directed(3, (0, 1, 1), (1, 2, -3), (2, 0, 1))),
        new("zero weights", () => Undirected(4, (0, 1, 0), (1, 2, 0), (2, 3, 0))),
        new("huge weights", () => Undirected(4, (0, 1, double.MaxValue), (1, 2, double.MaxValue), (2, 3, double.MaxValue))),
        new("tiny weights", () => Undirected(3, (0, 1, double.Epsilon), (1, 2, double.Epsilon))),
        new("dense random, 60 nodes", () => (Graph)new GenerateRandomGraphOperation(60, 500, seed: 3, maxWeight: 9).Execute(Graph.Empty(1)).NewState!),
    ];

    // ---------- tree ----------

    // Strict search-tree chains. Built directly: inserting one value at a time would take quadratic time.
    private static Tree Chain(int length, bool leftward) =>
        Tree.FromNodes(0, Enumerable.Range(0, length).Select(i => new TreeNodeData(
            i,
            leftward ? length - i : i,
            leftward && i + 1 < length ? i + 1 : -1,
            !leftward && i + 1 < length ? i + 1 : -1)).ToList());

    public static readonly IReadOnlyList<NamedDataset> Trees =
    [
        new("empty", Tree.Empty),
        new("single node", () => Tree.FromValues([42])),
        new("duplicates", () => Tree.FromValues([5, 5, 5, 5])),
        new("extreme values", () => Tree.FromValues([0, int.MinValue, int.MaxValue])),
        new("balanced 15", () => Tree.FromValues([8, 4, 12, 2, 6, 10, 14, 1, 3, 5, 7, 9, 11, 13, 15])),
        new("left chain of 50,000", () => Chain(50_000, leftward: true)),
        new("right chain of 50,000", () => Chain(50_000, leftward: false)),
    ];
}
