using Engine.Domain.Datasets;
using Xunit;

namespace Engine.Domain.Tests;

public class MatrixTests
{
    [Fact]
    public void NewMatrix_AllCellsDefaultToZero()
    {
        var matrix = new Matrix(rows: 3, columns: 4);

        Assert.Equal(0, matrix[0, 0]);
        Assert.Equal(0, matrix[2, 3]);
    }

    [Fact]
    public void SetCell_ThenGetCell_ReturnsSetValue()
    {
        var matrix = new Matrix(rows: 3, columns: 4);

        matrix[1, 2] = 42;

        Assert.Equal(42, matrix[1, 2]);
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(3, -1)]
    [InlineData(0, 0)]
    public void Constructor_RejectsNonPositiveDimensions(int rows, int columns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Matrix(rows, columns));
    }
}