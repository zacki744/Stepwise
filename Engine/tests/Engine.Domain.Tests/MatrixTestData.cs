using Engine.Domain.Datasets;

namespace Engine.Domain.Tests;

internal static class MatrixTestData
{
    public static Matrix From(params int[][] rows)
    {
        var matrix = new Matrix(rows.Length, rows[0].Length);
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                matrix[r, c] = rows[r][c];
        return matrix;
    }

    public static int[][] Rows(Matrix matrix) =>
        Enumerable.Range(0, matrix.Rows)
            .Select(r => Enumerable.Range(0, matrix.Columns).Select(c => matrix[r, c]).ToArray())
            .ToArray();
}