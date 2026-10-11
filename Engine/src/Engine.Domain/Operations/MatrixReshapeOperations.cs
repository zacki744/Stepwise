using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record MatrixReshapedPayload(int FromRows, int FromColumns, int ToRows, int ToColumns);

public enum FlipAxis { Horizontal, Vertical }

public sealed class TransposeMatrixOperation : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("transpose", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        context.Checkpoint((long)input.Rows * input.Columns);
        var result = new Matrix(input.Columns, input.Rows);
        for (int r = 0; r < input.Rows; r++)
            for (int c = 0; c < input.Columns; c++)
                result[c, r] = input[r, c];

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("MatrixReshaped", $"Transposed {input.Rows}x{input.Columns} -> {input.Columns}x{input.Rows}",
            new MatrixReshapedPayload(input.Rows, input.Columns, input.Columns, input.Rows)));
        return new OperationExecutionResult(true, result, null, trace, null);
    }
}

/// <summary>Rotates clockwise by quarter turns. Any integer works (-1 = counter-clockwise, 5 = 1).</summary>
public sealed class RotateMatrixOperation(int quarterTurns = 1) : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("rotate", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        int rows = input.Rows, cols = input.Columns;
        var result = turns is 1 or 3 ? new Matrix(cols, rows) : new Matrix(rows, cols);
        context.Checkpoint((long)rows * cols);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int v = input[r, c];
                switch (turns)
                {
                    case 0: result[r, c] = v; break;
                    case 1: result[c, rows - 1 - r] = v; break;
                    case 2: result[rows - 1 - r, cols - 1 - c] = v; break;
                    default: result[cols - 1 - c, r] = v; break;
                }
            }
        }

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("MatrixReshaped", $"Rotated {turns * 90} degrees clockwise",
            new MatrixReshapedPayload(rows, cols, result.Rows, result.Columns)));
        return new OperationExecutionResult(true, result, null, trace, null);
    }
}

/// <summary>"horizontal" mirrors left-to-right; "vertical" mirrors top-to-bottom.</summary>
public sealed class FlipMatrixOperation : IOperation<Matrix>
{
    private readonly FlipAxis _axis;

    public FlipMatrixOperation(string axis = "horizontal")
    {
        ArgumentNullException.ThrowIfNull(axis);
        _axis = axis.ToLowerInvariant() switch
        {
            "horizontal" => FlipAxis.Horizontal,
            "vertical" => FlipAxis.Vertical,
            _ => throw new ArgumentException($"Unknown axis '{axis}'; expected horizontal or vertical")
        };
    }

    public OperationMetadata Metadata { get; } = new("flip", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        int rows = input.Rows, cols = input.Columns;
        context.Checkpoint((long)rows * cols);
        var result = new Matrix(rows, cols);

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result[_axis == FlipAxis.Vertical ? rows - 1 - r : r,
                       _axis == FlipAxis.Horizontal ? cols - 1 - c : c] = input[r, c];

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("MatrixReshaped", $"Flipped {_axis.ToString().ToLowerInvariant()}ly",
            new MatrixReshapedPayload(rows, cols, rows, cols)));
        return new OperationExecutionResult(true, result, null, trace, null);
    }
}

/// <summary>Keeps only the given rectangle, as a new, smaller matrix.</summary>
public sealed class CropMatrixOperation(int row, int column, int height, int width) : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("crop", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        input.EnsureRegion(row, column, height, width);
        context.Checkpoint((long)height * width);

        var result = new Matrix(height, width);
        for (int r = 0; r < height; r++)
            for (int c = 0; c < width; c++)
                result[r, c] = input[row + r, column + c];

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("MatrixReshaped", $"Cropped {input.Rows}x{input.Columns} -> {height}x{width}",
            new MatrixReshapedPayload(input.Rows, input.Columns, height, width)));
        return new OperationExecutionResult(true, result, null, trace, null);
    }
}