using Engine.Domain.Datasets;
using Engine.Domain.Tracing;
using static Engine.Domain.Operations.FillMatrixOperation;

namespace Engine.Domain.Operations;

public sealed record GenerationCompletedPayload(int Generation, int Alive, int Changed);
public sealed record LifeResult(int GenerationsRun, int Alive, bool Stable);
public sealed record CellOpenedPayload(int Row, int Column);

public sealed class GameOfLifeOperation : IOperation<Matrix>
{
    public const int MaxGenerations = 1_000;

    private readonly int _generations;
    private readonly bool _wrap;

    public GameOfLifeOperation(int generations = 1, bool wrap = false)
    {
        if (generations < 1 || generations > MaxGenerations)
            throw new ArgumentException($"generations must be between 1 and {MaxGenerations} (got {generations})");
        _generations = generations;
        _wrap = wrap;
    }

    public OperationMetadata Metadata { get; } = new("game-of-life-step", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        int rows = input.Rows, cols = input.Columns;
        if (_wrap && (rows < 3 || cols < 3))
            throw new ArgumentException("wrap needs at least 3 rows and 3 columns (on smaller grids a cell would be its own neighbour)");

        var trace = new TraceRecorder(context);
        var current = new int[rows * cols];
        var next = new int[rows * cols];
        int alive = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int v = input[r, c];
                current[r * cols + c] = v;
                if (v != 0) alive++;
            }
        }

        int generationsRun = 0;
        bool stable = false;

        for (int generation = 1; generation <= _generations; generation++)
        {
            context.Checkpoint(current.Length);
            int changed = 0, aliveNext = 0;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int neighbors = 0;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int nr = r + dr, nc = c + dc;
                            if (_wrap) { nr = (nr + rows) % rows; nc = (nc + cols) % cols; }
                            else if ((uint)nr >= (uint)rows || (uint)nc >= (uint)cols) continue;
                            if (current[nr * cols + nc] != 0) neighbors++;
                        }
                    }

                    int index = r * cols + c;
                    int newValue = neighbors == 3 || (current[index] != 0 && neighbors == 2) ? 1 : 0;
                    next[index] = newValue;
                    if (newValue != 0) aliveNext++;
                    if (newValue != current[index])
                    {
                        changed++;
                        trace.Add(new DomainStepEvent("CellChanged", $"({r},{c}): {current[index]} -> {newValue}",
                            new CellChangedPayload(r, c, current[index], newValue)));
                    }
                }
            }

            if (changed == 0) { stable = true; break; } // nothing moves any more: stop early

            generationsRun++;
            trace.Add(new DomainStepEvent("GenerationCompleted", $"Generation {generation}: {aliveNext} alive, {changed} changed",
                new GenerationCompletedPayload(generation, aliveNext, changed)));
            (current, next) = (next, current);
            alive = aliveNext;
        }

        var result = new Matrix(rows, cols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result[r, c] = current[r * cols + c];

        return new OperationExecutionResult(true, result, new LifeResult(generationsRun, alive, stable), trace, null);
    }
}

/// <summary>
/// Generation: a perfect maze drawn onto a grid (1 = wall, 0 = open) by randomized DFS. width x height
/// are the maze's CELLS; the matrix is (2*height+1) x (2*width+1), with cell (x,y) at (2y+1, 2x+1).
/// Entrance and exit are conventionally (1,1) and (rows-2, cols-2). Works directly with
/// grid-shortest-path and matrix-to-graph. Ignores the input matrix; same seed, same maze.
/// </summary>
public sealed class GenerateMazeGridOperation(int width, int height, int seed) : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("generate-maze-grid", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        if (width < 1 || height < 1)
            throw new ArgumentException($"width and height must be positive (got {width}x{height})");

        long gridRows = 2L * height + 1, gridCols = 2L * width + 1;
        DatasetLimits.EnsureWithinLimit(gridRows * gridCols, $"Maze grid {gridRows}x{gridCols}");
        int rows = (int)gridRows, cols = (int)gridCols;

        var grid = new Matrix(rows, cols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                grid[r, c] = 1;

        var trace = new TraceRecorder(context);
        void Open(int r, int c)
        {
            grid[r, c] = 0;
            trace.Add(new DomainStepEvent("CellOpened", $"Opened ({r},{c})", new CellOpenedPayload(r, c)));
        }

        var random = new DeterministicRandom(seed);
        var visited = new bool[width * height];
        var stack = new Stack<int>();
        var options = new List<int>(4);

        visited[0] = true;
        stack.Push(0);
        Open(1, 1);

        while (stack.Count > 0)
        {
            int cell = stack.Peek();
            int cy = cell / width, cx = cell % width;

            options.Clear();
            if (cy > 0 && !visited[cell - width]) options.Add(cell - width);
            if (cy < height - 1 && !visited[cell + width]) options.Add(cell + width);
            if (cx > 0 && !visited[cell - 1]) options.Add(cell - 1);
            if (cx < width - 1 && !visited[cell + 1]) options.Add(cell + 1);

            if (options.Count == 0) { stack.Pop(); continue; }

            int chosen = options[random.Next(options.Count)];
            int ny = chosen / width, nx = chosen % width;
            visited[chosen] = true;

            Open(cy + ny + 1, cx + nx + 1); // the wall between the two cells
            Open(2 * ny + 1, 2 * nx + 1);   // the new cell
            stack.Push(chosen);
        }

        return new OperationExecutionResult(true, grid, null, trace, null);
    }
}

/// <summary>Fills every cell with a seeded-random value in [0, maxValue). Keeps the dimensions. maxValue 2 gives 0/1 noise, handy for Life.</summary>
public sealed class RandomFillOperation : IOperation<Matrix>
{
    private readonly int _seed;
    private readonly int _maxValue;

    public RandomFillOperation(int seed, int maxValue = 2)
    {
        if (maxValue < 1) throw new ArgumentException($"maxValue must be at least 1 (got {maxValue})");
        _seed = seed;
        _maxValue = maxValue;
    }

    public OperationMetadata Metadata { get; } = new("random-fill", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        var trace = new TraceRecorder(context);
        var random = new DeterministicRandom(_seed);
        var result = new Matrix(input.Rows, input.Columns);

        for (int r = 0; r < result.Rows; r++)
        {
            for (int c = 0; c < result.Columns; c++)
            {
                int value = random.Next(_maxValue);
                result[r, c] = value;
                trace.Add(new DomainStepEvent("CellFilled", $"({r},{c}) = {value}", new CellFilledPayload(r, c, value)));
            }
        }

        return new OperationExecutionResult(true, result, null, trace, null);
    }
}