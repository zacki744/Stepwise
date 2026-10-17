using System.Globalization;
using Engine.Domain;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;

namespace Engine.Gateway;

/// <summary>A problem in imported text. The message starts with the 1-based physical line, so blank and comment lines count.</summary>
public sealed class TextImportException(int line, string message) : ArgumentException($"line {line}: {message}")
{
    public int Line { get; } = line;
}

/// <summary>
/// Turns pasted text into datasets, so a client can send a big dataset as plain text instead of hand-built
/// JSON. Shared rules: values are separated by spaces, tabs or commas; lines end with \n or \r\n; blank lines
/// are skipped; everything after '#' on a line is a comment; errors name the line they are on.
///
///   matrix   one row per line:                 "1 2 3" / "4 5 6"
///   graph    edgeList (default):               "from to" or "from to weight", one connection per line
///            adjacencyList:                    "node: neighbour neighbour ..." (no weights; "4:" is a lone node)
///   tree     any number of integers, inserted in the order given
///
/// Graphs are undirected unless told otherwise: an undirected connection is stored in both directions, and the
/// same pair given twice (0 1, then 1 0) is one connection. Two different weights for it are an error rather
/// than a silent pick. Directed graphs keep every line as its own edge.
///
/// The text is scanned in place: no line or token is copied into a string of its own, so memory use stays
/// proportional to the dataset being built, not to how the text happens to be laid out (a single enormous line
/// costs no more than the same values spread over many). Limits are checked while reading, so an oversized
/// paste is refused at the line that crosses the limit instead of after being fully parsed.
/// </summary>
public static class TextImport
{
    private const int TokenPreview = 40;

    public static Matrix ParseMatrix(string text, OperationContext? context = null)
    {
        var work = new WorkMeter(context);
        var rows = new List<int[]>();
        int columns = -1;
        long cells = 0;
        var row = new List<int>();

        foreach (var line in Lines(text))
        {
            row.Clear();
            int pos = line.Start;
            while (NextToken(text, line.End, ref pos, out var start, out var length))
            {
                if (++cells > DatasetLimits.MaxCells)
                    throw new TextImportException(line.Number, $"the grid would exceed the limit of {DatasetLimits.MaxCells:N0} cells");
                row.Add(ParseInt(text, start, length, line.Number));
                work.Tick();
                if (columns >= 0 && row.Count > columns) break; // already too long; the exact count is only needed for the message
            }

            if (columns < 0) columns = row.Count;
            else if (row.Count != columns)
                throw new TextImportException(line.Number,
                    $"expected {columns} values like the first row, but found {(row.Count > columns ? CountTokens(text, line) : row.Count)}");

            rows.Add(row.ToArray());
        }
        work.Flush();

        if (rows.Count == 0) throw new ArgumentException("The grid text contains no rows");

        var matrix = new Matrix(rows.Count, columns);
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < columns; c++)
                matrix[r, c] = rows[r][c];
        return matrix;
    }

    public static Tree ParseTree(string text, OperationContext? context = null) =>
        Tree.FromValues(TreeValues(text, new WorkMeter(context)));

    private static IEnumerable<int> TreeValues(string text, WorkMeter work)
    {
        foreach (var line in Lines(text))
        {
            int pos = line.Start;
            while (NextToken(text, line.End, ref pos, out var start, out var length))
            {
                work.Tick();
                yield return ParseInt(text, start, length, line.Number);
            }
        }
        work.Flush();
    }

    public static Graph ParseGraph(string text, string format, bool directed, int? nodeCount, OperationContext? context = null)
    {
        var work = new WorkMeter(context);
        var connections = new List<(int Line, int From, int To, double Weight)>();
        long maxNode = -1;

        switch (format)
        {
            case "edgeList":
            {
                var bounds = new int[8]; // start/length of up to four tokens
                foreach (var line in Lines(text))
                {
                    int pos = line.Start, found = 0;
                    while (found < 4 && NextToken(text, line.End, ref pos, out var start, out var length))
                    {
                        bounds[found * 2] = start;
                        bounds[found * 2 + 1] = length;
                        found++;
                    }
                    if (found is < 2 or > 3)
                        throw new TextImportException(line.Number,
                            $"expected 'from to' or 'from to weight' but found {(found > 3 ? CountTokens(text, line) : found)} value(s)");

                    int from = ParseNode(text, bounds[0], bounds[1], line.Number);
                    int to = ParseNode(text, bounds[2], bounds[3], line.Number);
                    double weight = found == 3 ? ParseWeight(text, bounds[4], bounds[5], line.Number) : 1.0;
                    maxNode = Math.Max(maxNode, Math.Max(from, to));
                    connections.Add((line.Number, from, to, weight));
                    EnsureEdgeCount(connections.Count, line.Number);
                    work.Tick();
                }
                break;
            }

            case "adjacencyList":
                foreach (var line in Lines(text))
                {
                    int length = line.End - line.Start;
                    int colon = text.IndexOf(':', line.Start, length);
                    if (colon < 0 || text.IndexOf(':', colon + 1, line.End - colon - 1) >= 0)
                        throw new TextImportException(line.Number, "expected 'node: neighbour neighbour ...' with exactly one ':'");

                    int ownerPos = line.Start;
                    if (!NextToken(text, colon, ref ownerPos, out var ownerStart, out var ownerLength)
                        || NextToken(text, colon, ref ownerPos, out _, out _))
                        throw new TextImportException(line.Number, "expected a single node number before the ':'");

                    int from = ParseNode(text, ownerStart, ownerLength, line.Number);
                    maxNode = Math.Max(maxNode, from);

                    int pos = colon + 1;
                    while (NextToken(text, line.End, ref pos, out var start, out var tokenLength))
                    {
                        int to = ParseNode(text, start, tokenLength, line.Number);
                        maxNode = Math.Max(maxNode, to);
                        connections.Add((line.Number, from, to, 1.0));
                        EnsureEdgeCount(connections.Count, line.Number);
                        work.Tick();
                    }
                }
                break;

            default:
                throw new ArgumentException($"Unknown graph text format '{format}' (use 'edgeList' or 'adjacencyList')");
        }
        work.Flush();

        if (maxNode < 0 && nodeCount is null)
            throw new ArgumentException("The graph text contains no nodes or connections");

        long needed = maxNode + 1;
        long count = nodeCount ?? needed;
        if (count < needed)
            throw new ArgumentException($"nodeCount {count} is too small: the text uses node {maxNode}");
        if (count > int.MaxValue)
            throw new ArgumentException($"The graph text names node {maxNode}, which is far beyond the supported size");

        return new Graph((int)count, directed ? connections.Select(c => (c.From, c.To, c.Weight)) : Undirected(connections, work));
    }

    private static List<(int From, int To, double Weight)> Undirected(
        List<(int Line, int From, int To, double Weight)> connections, WorkMeter work)
    {
        var seen = new Dictionary<(int, int), (int Line, double Weight)>();
        var edges = new List<(int From, int To, double Weight)>();

        foreach (var (line, from, to, weight) in connections)
        {
            work.Tick();
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
        work.Flush();
        return edges;
    }

    // ---------- scanning ----------

    /// <summary>Charges the shared work budget in batches, so the time limit is honoured without a call per value.</summary>
    private sealed class WorkMeter(OperationContext? context)
    {
        private const int Batch = 1_024;
        private int _pending;

        public void Tick()
        {
            if (context is null || ++_pending < Batch) return;
            context.Checkpoint(_pending);
            _pending = 0;
        }

        public void Flush()
        {
            if (context is null || _pending == 0) return;
            context.Checkpoint(_pending);
            _pending = 0;
        }
    }

    /// <summary>The bounds of a line's content inside the text: comment removed, never copied.</summary>
    private readonly record struct TextLine(int Number, int Start, int End);

    private static IEnumerable<TextLine> Lines(string text)
    {
        int number = 0, pos = 0;
        while (pos < text.Length)
        {
            number++;
            int newline = text.IndexOf('\n', pos);
            int lineEnd = newline < 0 ? text.Length : newline;
            int hash = text.IndexOf('#', pos, lineEnd - pos);
            int contentEnd = hash >= 0 ? hash : lineEnd;

            if (SkipSeparators(text, pos, contentEnd) < contentEnd) // skip blank and comment-only lines
                yield return new TextLine(number, pos, contentEnd);

            if (newline < 0) yield break;
            pos = newline + 1;
        }
    }

    private static bool IsSeparator(char c) => c == ',' || char.IsWhiteSpace(c);

    private static int SkipSeparators(string text, int pos, int end)
    {
        while (pos < end && IsSeparator(text[pos])) pos++;
        return pos;
    }

    private static bool NextToken(string text, int end, ref int pos, out int start, out int length)
    {
        pos = SkipSeparators(text, pos, end);
        start = pos;
        while (pos < end && !IsSeparator(text[pos])) pos++;
        length = pos - start;
        return length > 0;
    }

    private static int CountTokens(string text, TextLine line)
    {
        int count = 0, pos = line.Start;
        while (NextToken(text, line.End, ref pos, out _, out _)) count++;
        return count;
    }

    // ---------- values ----------

    private static void EnsureEdgeCount(int count, int line)
    {
        if (count > DatasetLimits.MaxEdges)
            throw new TextImportException(line, $"the graph would exceed the limit of {DatasetLimits.MaxEdges:N0} directed edges");
    }

    /// <summary>A token for an error message: long ones are cut, so a huge bad value cannot balloon the reply.</summary>
    private static string Preview(string text, int start, int length) =>
        length <= TokenPreview
            ? text.Substring(start, length)
            : string.Concat(text.AsSpan(start, TokenPreview), "...");

    private static int ParseInt(string text, int start, int length, int line) =>
        int.TryParse(text.AsSpan(start, length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new TextImportException(line, $"'{Preview(text, start, length)}' is not a whole number");

    private static int ParseNode(string text, int start, int length, int line)
    {
        var node = ParseInt(text, start, length, line);
        if (node < 0) throw new TextImportException(line, $"node numbers cannot be negative (found {node})");
        return node;
    }

    private static double ParseWeight(string text, int start, int length, int line) =>
        double.TryParse(text.AsSpan(start, length), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) && double.IsFinite(weight)
            ? weight
            : throw new TextImportException(line, $"'{Preview(text, start, length)}' is not a usable weight");
}
