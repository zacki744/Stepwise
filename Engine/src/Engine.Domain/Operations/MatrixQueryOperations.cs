using Engine.Domain.Datasets;
using Engine.Domain.Tracing;
using static Engine.Domain.Operations.MatrixToGraphOperation;

namespace Engine.Domain.Operations;

public enum AggregateScope { All, Row, Column }

public sealed record AggregateResult(string Scope, int? Index, long Count, long Sum, int Min, int Max);
public sealed record ValueFoundPayload(int Row, int Column);
public sealed record FindValueResult(int TotalMatches, IReadOnlyList<GridPosition> Positions, bool Truncated);
public sealed record RegionFoundPayload(int Index, int Value, int Size, int Row, int Column);
public sealed record RegionsResult(int RegionCount, int LargestRegionSize);
public sealed record CellVisitedPayload(int Row, int Column, int Distance);

/// <summary>Sum / min / max / count over the whole matrix, one row, or one column. Sum is 64-bit, so it can't overflow.</summary>
public sealed class AggregateMatrixOperation : IOperation<Matrix>
{
    private readonly AggregateScope _scope;
    private readonly int _index;

    public AggregateMatrixOperation(string scope = "all", int index = 0)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope.ToLowerInvariant() switch
        {
            "all" => AggregateScope.All,
            "row" => AggregateScope.Row,
            "column" => AggregateScope.Column,
            _ => throw new ArgumentException($"Unknown scope '{scope}'; expected all, row or column")
        };
        _index = index;
    }

    public OperationMetadata Metadata { get; } = new("aggregate", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        int rowStart = 0, rowEnd = input.Rows, colStart = 0, colEnd = input.Columns;
        switch (_scope)
        {
            case AggregateScope.Row:
                if ((uint)_index >= (uint)input.Rows)
                    throw new ArgumentException($"row index {_index} is outside 0..{input.Rows - 1}");
                rowStart = _index;
                rowEnd = _index + 1;
                break;
            case AggregateScope.Column:
                if ((uint)_index >= (uint)input.Columns)
                    throw new ArgumentException($"column index {_index} is outside 0..{input.Columns - 1}");
                colStart = _index;
                colEnd = _index + 1;
                break;
        }

        long count = (long)(rowEnd - rowStart) * (colEnd - colStart);
        context.Checkpoint(count);

        long sum = 0;
        int min = int.MaxValue, max = int.MinValue;
        for (int r = rowStart; r < rowEnd; r++)
        {
            for (int c = colStart; c < colEnd; c++)
            {
                int v = input[r, c];
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("Aggregated", $"{count} cell(s): sum {sum}, min {min}, max {max}"));
        var result = new AggregateResult(_scope.ToString().ToLowerInvariant(),
            _scope == AggregateScope.All ? null : _index, count, sum, min, max);
        return new OperationExecutionResult(true, null, result, trace, null);
    }
}

/// <summary>
/// Finds every cell holding a value, scanning row by row. Positions are capped at `limit` (default
/// 1,000, maximum DatasetLimits.MaxReturnedItems) so the reply stays small; TotalMatches is always exact.
/// </summary>
public sealed class FindValueOperation : IOperation<Matrix>
{
    private readonly int _value;
    private readonly int _limit;

    public FindValueOperation(int value, int limit = 1_000)
    {
        if (limit < 1 || limit > DatasetLimits.MaxReturnedItems)
            throw new ArgumentException($"limit must be between 1 and {DatasetLimits.MaxReturnedItems:N0} (got {limit})");
        _value = value;
        _limit = limit;
    }

    public OperationMetadata Metadata { get; } = new("find-value", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        context.Checkpoint((long)input.Rows * input.Columns);
        var trace = new TraceRecorder(context);
        var positions = new List<GridPosition>();
        int total = 0;

        for (int r = 0; r < input.Rows; r++)
        {
            for (int c = 0; c < input.Columns; c++)
            {
                if (input[r, c] != _value) continue;
                total++;
                if (positions.Count < _limit) positions.Add(new GridPosition(r, c));
                trace.Add(new DomainStepEvent("ValueFound", $"{_value} at ({r},{c})", new ValueFoundPayload(r, c)));
            }
        }

        return new OperationExecutionResult(true, null,
            new FindValueResult(total, positions, Truncated: total > positions.Count), trace, null);
    }
}

/// <summary>
/// Counts connected regions of equal value (4-connected, or 8-connected with diagonals). With `value`
/// set, only regions of that value are counted. Iterative, so region shape cannot overflow the stack.
/// </summary>
public sealed class CountRegionsOperation(bool diagonals = false, int? value = null) : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("count-regions", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        int rows = input.Rows, cols = input.Columns;
        context.Checkpoint((long)rows * cols);

        var trace = new TraceRecorder(context);
        var visited = new bool[rows * cols];
        var stack = new Stack<int>();
        int regionCount = 0, largest = 0;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int start = r * cols + c;
                if (visited[start]) continue;

                int regionValue = input[r, c];
                if (value is int only && regionValue != only) continue;

                visited[start] = true;
                stack.Push(start);
                int size = 0;

                while (stack.Count > 0)
                {
                    int cell = stack.Pop();
                    size++;
                    int cr = cell / cols, cc = cell % cols;

                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if ((dr == 0 && dc == 0) || (!diagonals && dr != 0 && dc != 0)) continue;
                            int nr = cr + dr, nc = cc + dc;
                            if ((uint)nr >= (uint)rows || (uint)nc >= (uint)cols) continue;
                            int neighbor = nr * cols + nc;
                            if (visited[neighbor] || input[nr, nc] != regionValue) continue;
                            visited[neighbor] = true;
                            stack.Push(neighbor);
                        }
                    }
                }

                regionCount++;
                largest = Math.Max(largest, size);
                trace.Add(new DomainStepEvent("RegionFound", $"Region {regionCount}: value {regionValue}, {size} cell(s)",
                    new RegionFoundPayload(regionCount, regionValue, size, r, c)));
            }
        }

        return new OperationExecutionResult(true, null, new RegionsResult(regionCount, largest), trace, null);
    }
}

public sealed record GridPathResult(bool Found, int Length, IReadOnlyList<GridPosition>? Path, bool PathOmitted);

/// <summary>
/// Shortest path on the grid by BFS. Cells holding 0 are open and anything else is a wall (the same
/// convention as matrix-to-graph). With `diagonals` movement is 8-connected, matching count-regions.
/// Length counts moves. The path itself is omitted (PathOmitted=true) when it exceeds
/// DatasetLimits.MaxReturnedItems cells. A blocked endpoint or no route is a result, not an error.
/// </summary>
public sealed class GridShortestPathOperation(
    int startRow, int startColumn, int targetRow, int targetColumn, bool diagonals = false) : IOperation<Matrix>
{
    private const int Unvisited = -2;
    private const int StartMarker = -1;

    public OperationMetadata Metadata { get; } = new("grid-shortest-path", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        input.EnsureCell(startRow, startColumn);
        input.EnsureCell(targetRow, targetColumn);

        var trace = new TraceRecorder(context);
        var notFound = new OperationExecutionResult(true, null, new GridPathResult(false, 0, null, false), trace, null);

        if (input[startRow, startColumn] != 0 || input[targetRow, targetColumn] != 0)
        {
            trace.Add(new DomainStepEvent("EndpointBlocked", "The start or target cell is a wall"));
            return notFound;
        }

        int rows = input.Rows, cols = input.Columns;
        int start = startRow * cols + startColumn;
        int target = targetRow * cols + targetColumn;

        var previous = new int[rows * cols];
        Array.Fill(previous, Unvisited);
        var distance = new int[rows * cols];
        var queue = new Queue<int>();
        previous[start] = StartMarker;
        queue.Enqueue(start);
        bool found = false;

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            int cr = cell / cols, cc = cell % cols;
            trace.Add(new DomainStepEvent("CellVisited", $"Visited ({cr},{cc}) at distance {distance[cell]}",
                new CellVisitedPayload(cr, cc, distance[cell])));

            if (cell == target) { found = true; break; }

            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if ((dr == 0 && dc == 0) || (!diagonals && dr != 0 && dc != 0)) continue;
                    int nr = cr + dr, nc = cc + dc;
                    if ((uint)nr >= (uint)rows || (uint)nc >= (uint)cols) continue;
                    int neighbor = nr * cols + nc;
                    if (previous[neighbor] != Unvisited || input[nr, nc] != 0) continue;
                    previous[neighbor] = cell;
                    distance[neighbor] = distance[cell] + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (!found)
        {
            trace.Add(new DomainStepEvent("NoPath", "No route exists between the two cells"));
            return notFound;
        }

        int length = distance[target];
        List<GridPosition>? path = null;
        if (length + 1 <= DatasetLimits.MaxReturnedItems)
        {
            path = new List<GridPosition>(length + 1);
            for (int at = target; at != StartMarker; at = previous[at])
                path.Add(new GridPosition(at / cols, at % cols));
            path.Reverse();
        }

        trace.Add(new DomainStepEvent("PathFound", $"Path of {length} move(s)"));
        return new OperationExecutionResult(true, null, new GridPathResult(true, length, path, path is null), trace, null);
    }
}