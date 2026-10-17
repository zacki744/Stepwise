using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using static Engine.Gateway.OperationDsl;

namespace Engine.Gateway;

/// <summary>
/// Operations on a grid of integers. Each entry is the WHOLE definition of one operation: how it appears to a client (label, category,
/// description, options), a valid example, and how its options become an operation. Adding an operation
/// means adding one entry here; nothing else needs to be kept in step.
/// </summary>
internal static class MatrixOperationDefinitions
{
    public static IEnumerable<OperationDefinition> All()
    {
        yield return Define.For<Matrix>(
            Op("set-cell", "Set cell", Edit, "Write one value into one cell.",
                Req("row", OptionKinds.Row, "Row"), Req("column", OptionKinds.Column, "Column"), Req("value", OptionKinds.Int, "Value")),
            """{"row":0,"column":0,"value":7}""",
            options => new SetCellOperation(
                options["row"].GetInt32(), options["column"].GetInt32(), options["value"].GetInt32()));

        yield return Define.For<Matrix>(
            Op("fill-region", "Fill region", Edit, "Fill a rectangle with one value.",
                Req("row", OptionKinds.Row, "Top row"), Req("column", OptionKinds.Column, "Left column"),
                Req("height", OptionKinds.Int, "Height"), Req("width", OptionKinds.Int, "Width"), Req("value", OptionKinds.Int, "Value")),
            """{"row":1,"column":1,"height":2,"width":2,"value":9}""",
            options => new FillRegionOperation(
                options["row"].GetInt32(), options["column"].GetInt32(),
                options["height"].GetInt32(), options["width"].GetInt32(), options["value"].GetInt32()));

        yield return Define.For<Matrix>(
            Op("clear-matrix", "Clear matrix", Edit, "Set every cell to zero."),
            "{}",
            _ => new ClearMatrixOperation());

        yield return Define.For<Matrix>(
            Op("fill-even-odd", "Fill even/odd pattern", Edit, "Fill every cell: where row + column is even it gets 2, otherwise 1."),
            "{}",
            _ => new FillMatrixOperation());

        yield return Define.For<Matrix>(
            Op("flood-fill", "Flood fill", Edit, "Replace the connected area around a cell with a new value.",
                Req("row", OptionKinds.Row, "Row"), Req("column", OptionKinds.Column, "Column"), Req("newValue", OptionKinds.Int, "New value")),
            """{"row":0,"column":0,"newValue":5}""",
            options => new FloodFillOperation(
                options["row"].GetInt32(), options["column"].GetInt32(), options["newValue"].GetInt32()));

        yield return Define.For<Matrix>(
            Op("place-items", "Place items (bin packing)", Edit, "Pack rectangles onto the grid in the first spot they fit, largest first. Keeps what is already placed.",
                Req("items", OptionKinds.ItemList, "Items (id, width, height)")),
            """{"items":[{"id":1,"width":2,"height":2}]}""",
            options =>
                {
                    var items = options["items"].EnumerateArray()
                        .Select(e => new BinItem(
                            e.GetProperty("id").GetInt32(),
                            e.GetProperty("width").GetInt32(),
                            e.GetProperty("height").GetInt32()))
                        .ToList();
                    return new PlaceItemsOperation(items);
                });

        yield return Define.For<Matrix>(
            Op("random-fill", "Random fill", Generate, "Fill every cell with a seeded random value from 0 up to (not including) the limit.",
                Req("seed", OptionKinds.Seed, "Seed"), Opt("maxValue", OptionKinds.Int, "Values below", 2)),
            """{"seed":1,"maxValue":2}""",
            options => new RandomFillOperation(
                options["seed"].GetInt32(),
                options.TryGetValue("maxValue", out var maxValue) ? maxValue.GetInt32() : 2));

        yield return Define.For<Matrix>(
            Op("generate-maze-grid", "Generate maze", Generate, "Draw a seeded maze on a new grid (1 = wall, 0 = open). Width and height count maze cells; the grid is (2h+1) x (2w+1).",
                Req("width", OptionKinds.Int, "Width in cells"), Req("height", OptionKinds.Int, "Height in cells"), Req("seed", OptionKinds.Seed, "Seed")),
            """{"width":4,"height":3,"seed":1}""",
            options => new GenerateMazeGridOperation(
                options["width"].GetInt32(), options["height"].GetInt32(), options["seed"].GetInt32()));

        yield return Define.For<Matrix>(
            Op("transpose", "Transpose", Reshape, "Swap rows and columns."),
            "{}",
            _ => new TransposeMatrixOperation());

        yield return Define.For<Matrix>(
            Op("rotate", "Rotate", Reshape, "Rotate clockwise in quarter turns.",
                Opt("quarterTurns", OptionKinds.Int, "Quarter turns", 1)),
            """{"quarterTurns":1}""",
            options => new RotateMatrixOperation(
                options.TryGetValue("quarterTurns", out var turns) ? turns.GetInt32() : 1));

        yield return Define.For<Matrix>(
            Op("flip", "Flip", Reshape, "Mirror the grid left-to-right or top-to-bottom.",
                Pick("axis", "Axis", "horizontal", "horizontal", "vertical")),
            """{"axis":"vertical"}""",
            options => new FlipMatrixOperation(
                options.TryGetValue("axis", out var axis) ? axis.GetString() ?? "horizontal" : "horizontal"));

        yield return Define.For<Matrix>(
            Op("crop", "Crop", Reshape, "Keep only a rectangle of the grid.",
                Req("row", OptionKinds.Row, "Top row"), Req("column", OptionKinds.Column, "Left column"),
                Req("height", OptionKinds.Int, "Height"), Req("width", OptionKinds.Int, "Width")),
            """{"row":1,"column":1,"height":3,"width":4}""",
            options => new CropMatrixOperation(
                options["row"].GetInt32(), options["column"].GetInt32(),
                options["height"].GetInt32(), options["width"].GetInt32()));

        yield return Define.For<Matrix>(
            Op("aggregate", "Aggregate", Query, "Sum, minimum, maximum and count over the whole grid, one row or one column.",
                Pick("scope", "Scope", "all", "all", "row", "column"), Opt("index", OptionKinds.Int, "Row or column index", 0)),
            """{"scope":"row","index":1}""",
            options => new AggregateMatrixOperation(
                options.TryGetValue("scope", out var scope) ? scope.GetString() ?? "all" : "all",
                options.TryGetValue("index", out var index) ? index.GetInt32() : 0));

        yield return Define.For<Matrix>(
            Op("find-value", "Find value", Query, "Find every cell that holds a value.",
                Req("value", OptionKinds.Int, "Value"), Opt("limit", OptionKinds.Int, "Most results", 1_000)),
            """{"value":1,"limit":100}""",
            options => new FindValueOperation(
                options["value"].GetInt32(),
                options.TryGetValue("limit", out var limit) ? limit.GetInt32() : 1_000));

        yield return Define.For<Matrix>(
            Op("count-regions", "Count regions", Query, "Count connected areas of equal value.",
                Opt("diagonals", OptionKinds.Bool, "Count diagonal neighbours", false),
                Opt("value", OptionKinds.Int, "Only this value (empty = all)", null)),
            """{"diagonals":false,"value":0}""",
            options => new CountRegionsOperation(
                options.TryGetValue("diagonals", out var diagonals) && diagonals.GetBoolean(),
                options.TryGetValue("value", out var only) ? (int?)only.GetInt32() : null));

        yield return Define.For<Matrix>(
            Op("read-cell", "Read cell", Query, "Read one cell.",
                Req("row", OptionKinds.Row, "Row"), Req("column", OptionKinds.Column, "Column")),
            """{"row":0,"column":0}""",
            options => new ReadCellQuery(options["row"].GetInt32(), options["column"].GetInt32()));

        yield return Define.For<Matrix>(
            Op("game-of-life-step", "Game of Life", Query, "Advance Conway's Game of Life by some generations.",
                Opt("generations", OptionKinds.Int, "Generations", 1), Opt("wrap", OptionKinds.Bool, "Wrap around the edges", false)),
            """{"generations":3,"wrap":false}""",
            options => new GameOfLifeOperation(
                options.TryGetValue("generations", out var generations) ? generations.GetInt32() : 1,
                options.TryGetValue("wrap", out var wrap) && wrap.GetBoolean()));

        yield return Define.For<Matrix>(
            Op("grid-shortest-path", "Grid shortest path", Query, "Fewest-moves route between two cells; 0 is open and anything else is a wall.",
                Req("startRow", OptionKinds.Row, "Start row"), Req("startColumn", OptionKinds.Column, "Start column"),
                Req("targetRow", OptionKinds.Row, "Target row"), Req("targetColumn", OptionKinds.Column, "Target column"),
                Opt("diagonals", OptionKinds.Bool, "Allow diagonal moves", false)),
            """{"startRow":0,"startColumn":0,"targetRow":1,"targetColumn":1,"diagonals":false}""",
            options => new GridShortestPathOperation(
                options["startRow"].GetInt32(), options["startColumn"].GetInt32(),
                options["targetRow"].GetInt32(), options["targetColumn"].GetInt32(),
                options.TryGetValue("diagonals", out var moveDiagonally) && moveDiagonally.GetBoolean()));

        yield return Define.For<Matrix>(
            Op("matrix-to-graph", "Matrix to graph", Conversion, "Turn the open cells (0) into graph nodes linked to their open neighbours."),
            "{}",
            _ => new MatrixToGraphOperation());
    }
}
