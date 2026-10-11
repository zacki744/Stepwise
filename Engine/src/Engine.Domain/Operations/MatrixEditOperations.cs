using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record CellChangedPayload(int Row, int Column, int OldValue, int NewValue);
public sealed record MatrixClearedPayload(int CellsCleared);

/// <summary>Sets one cell. Value is the cell's previous value.</summary>
public sealed class SetCellOperation(int row, int column, int value) : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("set-cell", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        input.EnsureCell(row, column);
        var working = input.Clone();
        int previous = working[row, column];
        working[row, column] = value;

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("CellChanged", $"({row},{column}): {previous} -> {value}",
            new CellChangedPayload(row, column, previous, value)));
        return new OperationExecutionResult(true, working, previous, trace, null);
    }
}

/// <summary>Sets every cell of a rectangle. Value is how many cells actually changed.</summary>
public sealed class FillRegionOperation(int row, int column, int height, int width, int value) : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("fill-region", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        input.EnsureRegion(row, column, height, width);
        context.Checkpoint((long)height * width);

        var working = input.Clone();
        var trace = new TraceRecorder(context);
        int changed = 0;

        for (int r = row; r < row + height; r++)
        {
            for (int c = column; c < column + width; c++)
            {
                int previous = working[r, c];
                if (previous == value) continue;
                working[r, c] = value;
                changed++;
                trace.Add(new DomainStepEvent("CellChanged", $"({r},{c}): {previous} -> {value}",
                    new CellChangedPayload(r, c, previous, value)));
            }
        }

        return new OperationExecutionResult(true, working, changed, trace, null);
    }
}

/// <summary>Destruction: resets every cell to 0, keeping the dimensions. Value is how many non-zero cells were cleared.</summary>
public sealed class ClearMatrixOperation : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("clear-matrix", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        context.Checkpoint((long)input.Rows * input.Columns);

        int nonZero = 0;
        for (int r = 0; r < input.Rows; r++)
            for (int c = 0; c < input.Columns; c++)
                if (input[r, c] != 0) nonZero++;

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("MatrixCleared", $"Cleared {nonZero} non-zero cell(s)",
            new MatrixClearedPayload(nonZero)));
        return new OperationExecutionResult(true, new Matrix(input.Rows, input.Columns), nonZero, trace, null);
    }
}