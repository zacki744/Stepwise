using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

/// <summary>
/// An item to place: Id must be a positive, non-zero grid marker value
/// (0 means empty cell).
/// </summary>
public readonly record struct BinItem(int Id, int Width, int Height);

/// <summary>
/// First-fit-decreasing bin packing onto a grid. Places items largest-first
/// into the first position (scanning row-major) where they fit without
/// overlapping existing occupied cells. Non-destructive relative to any
/// items already placed on the input grid — it adds to the layout, it
/// doesn't clear it first.
/// </summary>
public sealed class PlaceItemsOperation(IReadOnlyList<BinItem> items) : IOperation<Matrix>
{
	public const int MaxItems = 1_000;
	public sealed record ItemPlacedPayload(int ItemId, int Row, int Column, int Width, int Height);
    public sealed record ItemUnplacedPayload(int ItemId);
    public OperationMetadata Metadata { get; } =
        new("place-items", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
		if (items.Count > MaxItems)
			throw new ArgumentException($"At most {MaxItems} items can be placed in one operation (got {items.Count})");
		var seenIds = new HashSet<int>();
		foreach (var item in items)
		{
			if (item.Id <= 0)
				throw new ArgumentException($"Item ids must be positive, since 0 marks an empty cell (got {item.Id})");
			if (item.Width <= 0 || item.Height <= 0)
				throw new ArgumentException($"Item {item.Id} has a non-positive size ({item.Width}x{item.Height})");
			if (!seenIds.Add(item.Id))
				throw new ArgumentException($"Duplicate item id {item.Id}");
		}
		var working = new Matrix(input.Rows, input.Columns);
        for (int r = 0; r < input.Rows; r++)
            for (int c = 0; c < input.Columns; c++)
                working[r, c] = input[r, c]; // preserve whatever was already placed

        var trace = new TraceRecorder(context);
        var unplacedIds = new List<int>();

        foreach (var item in items.OrderByDescending(i => (long)i.Width * i.Height))
        {
            var position = FindFit(working, item, context);
            if (position is null)
            {
                unplacedIds.Add(item.Id);
                trace.Add(new DomainStepEvent("ItemUnplaced", $"Item {item.Id} ({item.Width}x{item.Height}) did not fit",
                    new ItemUnplacedPayload(item.Id)));
                continue;
            }

            var (row, col) = position.Value;
            for (int r = row; r < row + item.Height; r++)
                for (int c = col; c < col + item.Width; c++)
                    working[r, c] = item.Id;

            trace.Add(new DomainStepEvent("ItemPlaced", $"Item {item.Id} at ({row},{col})",
                new ItemPlacedPayload(item.Id, row, col, item.Width, item.Height)));
        }

        return new OperationExecutionResult(
            Succeeded: true,
            NewState: working,
            Value: unplacedIds,
            Trace: trace,
            FailureReason: null);
    }

    private static (int Row, int Col)? FindFit(Matrix grid, BinItem item, OperationContext context)
    {
        for (int row = 0; row <= grid.Rows - item.Height; row++)
            for (int col = 0; col <= grid.Columns - item.Width; col++)
            {
                context.Checkpoint(); // one unit per candidate position
                if (FitsAt(grid, row, col, item))
                    return (row, col);
            }
        return null;
    }

    private static bool FitsAt(Matrix grid, int row, int col, BinItem item)
    {
        for (int r = row; r < row + item.Height; r++)
            for (int c = col; c < col + item.Width; c++)
                if (grid[r, c] != 0)
                    return false;
        return true;
    }
}