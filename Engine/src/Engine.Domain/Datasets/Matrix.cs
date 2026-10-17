using Engine.Domain.Capabilities;

namespace Engine.Domain.Datasets;

public readonly record struct Cell(int Row, int Column);

public sealed class Matrix : IDataset, ITraversable<Cell>
{
    private readonly int[] _cells;
    public long EstimatedBytes => 64L + 4L * _cells.Length;

    public int Rows { get; }
    public int Columns { get; }

    public Matrix(int rows, int columns)
    {
        if (rows <= 0 || columns <= 0)
            throw new ArgumentOutOfRangeException(
                rows <= 0 ? nameof(rows) : nameof(columns),
                "Matrix dimensions must be positive.");

        DatasetLimits.EnsureWithinLimit((long)rows * columns, $"Matrix {rows}x{columns}");
        
        Rows = rows;
        Columns = columns;
        _cells = new int[rows * columns]; // flat, array-backed — matches backend-design.md §3
    }

    public int this[int row, int column]
    {
        get => _cells[Index(row, column)];
        set => _cells[Index(row, column)] = value;
    }

    private int Index(int row, int column)
    {
        if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns)
            throw new ArgumentOutOfRangeException(
                row >= Rows ? nameof(row) : nameof(column));

        return row * Columns + column;
    }
    public IEnumerable<Cell> GetNeighbors(Cell node)
    {
        (int dr, int dc)[] directions = [(-1, 0), (1, 0), (0, -1), (0, 1)];

        foreach (var (dr, dc) in directions)
        {
            int r = node.Row + dr;
            int c = node.Column + dc;
            if ((uint)r < (uint)Rows && (uint)c < (uint)Columns)
                yield return new Cell(r, c);
        }
    }
    public Matrix Clone()
    {
        var copy = new Matrix(Rows, Columns);
        Array.Copy(_cells, copy._cells, _cells.Length);
        return copy;
    }

    public void EnsureCell(int row, int column)
    {
        if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns)
            throw new ArgumentException($"Cell ({row},{column}) is outside the {Rows}x{Columns} matrix");
    }

    public void EnsureRegion(int row, int column, int height, int width)
    {
        if (height < 1 || width < 1)
            throw new ArgumentException($"Region size must be positive (got {height}x{width})");
        if (row < 0 || column < 0 || (long)row + height > Rows || (long)column + width > Columns)
            throw new ArgumentException(
                $"Region {height}x{width} at ({row},{column}) does not fit inside the {Rows}x{Columns} matrix");
    }
}