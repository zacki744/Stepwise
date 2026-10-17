using System.Globalization;
using Engine.Domain;
using Engine.Domain.Datasets;

namespace Engine.Gateway;

/// <summary>A problem in imported text. The message starts with the 1-based physical line, so blank and comment lines count.</summary>
public sealed class TextImportException(int line, string message) : ArgumentException($"line {line}: {message}")
{
    public int Line { get; } = line;
}

/// <summary>
/// Turns pasted text into datasets, so a client can send a big dataset as plain text instead of hand-built
/// JSON. Shared rules: values are separated by spaces, tabs or commas; blank lines are skipped; everything
/// after '#' on a line is a comment; errors name the line they are on.
///
///   matrix   one row per line:                 "1 2 3" / "4 5 6"
///   graph    edgeList (default):               "from to" or "from to weight", one connection per line
///            adjacencyList:                    "node: neighbour neighbour ..." (no weights; "4:" is a lone node)
///   tree     any number of integers, inserted in the order given
///
/// Graphs are undirected unless told otherwise: an undirected connection is stored in both directions, and the
/// same pair given twice (0 1, then 1 0) is one connection. Two different weights for it are an error rather
/// than a silent pick. Directed graphs keep every line as its own edge.
/// </summary>
public static class TextImport
{
    public static Matrix ParseMatrix(string text)
    {
        var rows = new List<int[]>();
        int columns = -1;
        long cells = 0;

        foreach (var (number, content) in Lines(text))
        {
            var tokens = Tokenize(content);
            if (columns < 0) columns = tokens.Length;
            else if (tokens.Length != columns)
                throw new TextImportException(number, $"expected {columns} values like the first row, but found {tokens.Length}");

            cells += tokens.Length;
            if (cells > DatasetLimits.MaxCells)
                throw new TextImportException(number, $"the grid would exceed the limit of {DatasetLimits.MaxCells:N0} cells");

            var row = new int[tokens.Length];
            for (int i = 0; i < tokens.Length; i++) row[i] = ParseInt(tokens[i], number);
            rows.Add(row);
        }

        if (rows.Count == 0) throw new ArgumentException("The grid text contains no rows");

        var matrix = new Matrix(rows.Count, columns);
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < columns; c++)
                matrix[r, c] = rows[r][c];
        return matrix;
    }

    public static Tree ParseTree(string text) => Tree.FromValues(TreeValues(text));

    private static IEnumerable<int> TreeValues(string text)
    {
        foreach (var (number, content) in Lines(text))
            foreach (var token in Tokenize(content))
                yield return ParseInt(token, number);
    }

    public static Graph ParseGraph(string text, string format, bool directed, int? nodeCount)
    {
        var connections = new List<(int Line, int From, int To, double Weight)>();
        long maxNode = -1;

        switch (format)
        {
            case "edgeList":
                foreach (var (number, content) in Lines(text))
                {
                    var tokens = Tokenize(content);
                    if (tokens.Length is < 2 or > 3)
                        throw new TextImportException(number, $"expected 'from to' or 'from to weight' but found {tokens.Length} value(s)");
                    int from = ParseNode(tokens[0], number), to = ParseNode(tokens[1], number);
                    double weight = tokens.Length == 3 ? ParseWeight(tokens[2], number) : 1.0;
                    maxNode = Math.Max(maxNode, Math.Max(from, to));
                    connections.Add((number, from, to, weight));
                    EnsureEdgeCount(connections.Count, number);
                }
                break;

            case "adjacencyList":
                foreach (var (number, content) in Lines(text))
                {
                    int colon = content.IndexOf(':');
                    if (colon < 0 || content.IndexOf(':', colon + 1) >= 0)
                        throw new TextImportException(number, "expected 'node: neighbour neighbour ...' with exactly one ':'");
                    var owner = Tokenize(content[..colon]);
                    if (owner.Length != 1)
                        throw new TextImportException(number, "expected a single node number before the ':'");
                    int from = ParseNode(owner[0], number);
                    maxNode = Math.Max(maxNode, from);
                    foreach (var token in Tokenize(content[(colon + 1)..]))
                    {
                        int to = ParseNode(token, number);
                        maxNode = Math.Max(maxNode, to);
                        connections.Add((number, from, to, 1.0));
                        EnsureEdgeCount(connections.Count, number);
                    }
                }
                break;

            default:
                throw new ArgumentException($"Unknown graph text format '{format}' (use 'edgeList' or 'adjacencyList')");
        }

        if (maxNode < 0 && nodeCount is null)
            throw new ArgumentException("The graph text contains no nodes or connections");

        long needed = maxNode + 1;
        long count = nodeCount ?? needed;
        if (count < needed)
            throw new ArgumentException($"nodeCount {count} is too small: the text uses node {maxNode}");
        if (count > int.MaxValue)
            throw new ArgumentException($"The graph text names node {maxNode}, which is far beyond the supported size");

        return new Graph((int)count, directed ? connections.Select(c => (c.From, c.To, c.Weight)) : Undirected(connections));
    }

    private static List<(int From, int To, double Weight)> Undirected(
        List<(int Line, int From, int To, double Weight)> connections)
    {
        var seen = new Dictionary<(int, int), (int Line, double Weight)>();
        var edges = new List<(int From, int To, double Weight)>();

        foreach (var (line, from, to, weight) in connections)
        {
            var key = (Math.Min(from, to), Math.Max(from, to));
            if (seen.TryGetValue(key, out var earlier))
            {
                if (earlier.Weight != weight)
                    throw new TextImportException(line,
                        $"connection {from}-{to} was already given on line {earlier.Line} with weight {earlier.Weight.ToString(CultureInfo.InvariantCulture)}");
                continue; // the same connection listed from the other end
            }

            seen[key] = (line, weight);
            edges.Add((from, to, weight));
            if (from != to) edges.Add((to, from, weight));
            EnsureEdgeCount(edges.Count, line);
        }
        return edges;
    }

    // ---------- shared parsing ----------

    private static void EnsureEdgeCount(int count, int line)
    {
        if (count > DatasetLimits.MaxEdges)
            throw new TextImportException(line, $"the graph would exceed the limit of {DatasetLimits.MaxEdges:N0} directed edges");
    }

    private static IEnumerable<(int Number, string Content)> Lines(string text)
    {
        using var reader = new StringReader(text);
        int number = 0;
        while (reader.ReadLine() is { } line)
        {
            number++;
            int hash = line.IndexOf('#');
            var content = (hash >= 0 ? line[..hash] : line).Trim();
            if (content.Length > 0) yield return (number, content);
        }
    }

    private static string[] Tokenize(string content) =>
        content.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);

    private static int ParseInt(string token, int line) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new TextImportException(line, $"'{token}' is not a whole number");

    private static int ParseNode(string token, int line)
    {
        var node = ParseInt(token, line);
        if (node < 0) throw new TextImportException(line, $"node numbers cannot be negative (found {node})");
        return node;
    }

    private static double ParseWeight(string token, int line) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) && double.IsFinite(weight)
            ? weight
            : throw new TextImportException(line, $"'{token}' is not a usable weight");
}
