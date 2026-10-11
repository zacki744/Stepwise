using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class MatrixEditAndReshapeTests
{
    private static Matrix Sample() => MatrixTestData.From([1, 2, 3], [4, 5, 6]);
    private static int[][] Rows(Matrix m) => MatrixTestData.Rows(m);
    private static Matrix Rotated(int turns) =>
        Assert.IsType<Matrix>(new RotateMatrixOperation(turns).Execute(Sample()).NewState);

    // ---------- set-cell ----------

    [Fact]
    public void SetCell_ChangesOnlyThatCell_AndReportsThePreviousValue()
    {
        var result = new SetCellOperation(1, 2, 99).Execute(Sample());

        Assert.Equal(new[] { new[] { 1, 2, 3 }, new[] { 4, 5, 99 } }, Rows(Assert.IsType<Matrix>(result.NewState)));
        Assert.Equal(6, result.Value);
    }

    [Fact]
    public void SetCell_DoesNotMutateTheInput()
    {
        var input = Sample();
        new SetCellOperation(0, 0, 99).Execute(input);
        Assert.Equal(1, input[0, 0]);
    }

    [Fact]
    public void SetCell_OutOfRange_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SetCellOperation(2, 0, 1).Execute(Sample()));
    }

    // ---------- fill-region ----------

    [Fact]
    public void FillRegion_FillsTheRectangle_AndCountsChangedCells()
    {
        var result = new FillRegionOperation(row: 0, column: 1, height: 2, width: 2, value: 9).Execute(Sample());

        Assert.Equal(new[] { new[] { 1, 9, 9 }, new[] { 4, 9, 9 } }, Rows(Assert.IsType<Matrix>(result.NewState)));
        Assert.Equal(4, result.Value);
    }

    [Fact]
    public void FillRegion_WithTheValueAlreadyThere_ChangesNothing()
    {
        var result = new FillRegionOperation(0, 0, 1, 1, value: 1).Execute(Sample());
        Assert.Equal(0, result.Value);
    }

    [Theory]
    [InlineData(1, 2, 2, 2)] // runs off the bottom-right
    [InlineData(0, 0, 0, 1)] // zero height
    [InlineData(-1, 0, 1, 1)] // negative origin
    public void FillRegion_InvalidRegion_IsRejected(int row, int column, int height, int width)
    {
        Assert.Throws<ArgumentException>(() => new FillRegionOperation(row, column, height, width, 1).Execute(Sample()));
    }

    // ---------- clear-matrix ----------

    [Fact]
    public void Clear_ZeroesEverything_KeepsDimensions_AndCountsNonZeroCells()
    {
        var result = new ClearMatrixOperation().Execute(Sample());

        Assert.Equal(new[] { new[] { 0, 0, 0 }, new[] { 0, 0, 0 } }, Rows(Assert.IsType<Matrix>(result.NewState)));
        Assert.Equal(6, result.Value);
    }

    // ---------- transpose ----------

    [Fact]
    public void Transpose_SwapsRowsAndColumns()
    {
        var result = Assert.IsType<Matrix>(new TransposeMatrixOperation().Execute(Sample()).NewState);

        Assert.Equal(new[] { new[] { 1, 4 }, new[] { 2, 5 }, new[] { 3, 6 } }, Rows(result));
    }

    [Fact]
    public void Transpose_Twice_RestoresTheOriginal()
    {
        var once = Assert.IsType<Matrix>(new TransposeMatrixOperation().Execute(Sample()).NewState);
        var twice = Assert.IsType<Matrix>(new TransposeMatrixOperation().Execute(once).NewState);

        Assert.Equal(Rows(Sample()), Rows(twice));
    }

    // ---------- rotate ----------

    [Fact]
    public void Rotate_OneTurn_IsNinetyDegreesClockwise() =>
        Assert.Equal(new[] { new[] { 4, 1 }, new[] { 5, 2 }, new[] { 6, 3 } }, Rows(Rotated(1)));

    [Fact]
    public void Rotate_TwoTurns_IsOneHundredEightyDegrees() =>
        Assert.Equal(new[] { new[] { 6, 5, 4 }, new[] { 3, 2, 1 } }, Rows(Rotated(2)));

    [Fact]
    public void Rotate_ThreeTurns_IsNinetyDegreesCounterClockwise() =>
        Assert.Equal(new[] { new[] { 3, 6 }, new[] { 2, 5 }, new[] { 1, 4 } }, Rows(Rotated(3)));

    [Theory]
    [InlineData(1, 5)]
    [InlineData(3, -1)]
    [InlineData(2, -2)]
    [InlineData(0, 4)]
    public void Rotate_EquivalentTurnCounts_GiveTheSameResult(int canonical, int equivalent) =>
        Assert.Equal(Rows(Rotated(canonical)), Rows(Rotated(equivalent)));

    // ---------- flip ----------

    [Fact]
    public void Flip_Horizontal_MirrorsLeftToRight()
    {
        var result = Assert.IsType<Matrix>(new FlipMatrixOperation("horizontal").Execute(Sample()).NewState);
        Assert.Equal(new[] { new[] { 3, 2, 1 }, new[] { 6, 5, 4 } }, Rows(result));
    }

    [Fact]
    public void Flip_Vertical_MirrorsTopToBottom()
    {
        var result = Assert.IsType<Matrix>(new FlipMatrixOperation("vertical").Execute(Sample()).NewState);
        Assert.Equal(new[] { new[] { 4, 5, 6 }, new[] { 1, 2, 3 } }, Rows(result));
    }

    [Fact]
    public void Flip_UnknownAxis_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new FlipMatrixOperation("diagonal"));
    }

    // ---------- crop ----------

    [Fact]
    public void Crop_KeepsOnlyTheRectangle()
    {
        var result = Assert.IsType<Matrix>(new CropMatrixOperation(row: 0, column: 1, height: 2, width: 2).Execute(Sample()).NewState);
        Assert.Equal(new[] { new[] { 2, 3 }, new[] { 5, 6 } }, Rows(result));
    }

    [Fact]
    public void Crop_RegionOutsideTheMatrix_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new CropMatrixOperation(0, 2, 1, 2).Execute(Sample()));
    }
}