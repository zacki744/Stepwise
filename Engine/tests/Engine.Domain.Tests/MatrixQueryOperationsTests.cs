using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;
using static Engine.Domain.Operations.MatrixToGraphOperation;

namespace Engine.Domain.Tests;

public class MatrixQueryOperationsTests
{
    private static Matrix Sample() => MatrixTestData.From([1, 2, 3], [4, 5, 6]);

    // ---------- aggregate ----------

    [Fact]
    public void Aggregate_All()
    {
        var result = Assert.IsType<AggregateResult>(new AggregateMatrixOperation("all").Execute(Sample()).Value);
        Assert.Equal(new AggregateResult("all", null, Count: 6, Sum: 21, Min: 1, Max: 6), result);
    }

    [Fact]
    public void Aggregate_OneRow()
    {
        var result = Assert.IsType<AggregateResult>(new AggregateMatrixOperation("row", 1).Execute(Sample()).Value);
        Assert.Equal(new AggregateResult("row", 1, 3, 15, 4, 6), result);
    }

    [Fact]
    public void Aggregate_OneColumn()
    {
        var result = Assert.IsType<AggregateResult>(new AggregateMatrixOperation("column", 2).Execute(Sample()).Value);
        Assert.Equal(new AggregateResult("column", 2, 2, 9, 3, 6), result);
    }

    [Fact]
    public void Aggregate_IndexOutOfRange_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AggregateMatrixOperation("row", 2).Execute(Sample()));
    }

    [Fact]
    public void Aggregate_UnknownScope_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AggregateMatrixOperation("diagonal"));
    }

    [Fact]
    public void Aggregate_SumDoesNotOverflow()
    {
        var big = MatrixTestData.From([int.MaxValue, int.MaxValue]);
        var result = Assert.IsType<AggregateResult>(new AggregateMatrixOperation().Execute(big).Value);
        Assert.Equal(2L * int.MaxValue, result.Sum);
    }

    // ---------- find-value ----------

    [Fact]
    public void FindValue_ReturnsEveryMatchInRowOrder()
    {
        var grid = MatrixTestData.From([1, 2, 1], [2, 1, 2]);

        var result = Assert.IsType<FindValueResult>(new FindValueOperation(1).Execute(grid).Value);

        Assert.Equal(3, result.TotalMatches);
        Assert.False(result.Truncated);
        Assert.Equal(new[] { new GridPosition(0, 0), new GridPosition(0, 2), new GridPosition(1, 1) }, result.Positions);
    }

    [Fact]
    public void FindValue_BeyondTheLimit_TruncatesPositions_ButCountsEverything()
    {
        var grid = MatrixTestData.From([1, 2, 1], [2, 1, 2]);

        var result = Assert.IsType<FindValueResult>(new FindValueOperation(1, limit: 2).Execute(grid).Value);

        Assert.Equal(3, result.TotalMatches);
        Assert.Equal(2, result.Positions.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void FindValue_NoMatch_ReturnsZero()
    {
        var result = Assert.IsType<FindValueResult>(new FindValueOperation(99).Execute(Sample()).Value);
        Assert.Equal(0, result.TotalMatches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(DatasetLimits.MaxReturnedItems + 1)]
    public void FindValue_InvalidLimit_IsRejected(int limit)
    {
        Assert.Throws<ArgumentException>(() => new FindValueOperation(1, limit));
    }

    // ---------- count-regions ----------

    //   1 1 0
    //   0 1 0
    //   1 0 1
    private static Matrix RegionGrid() => MatrixTestData.From([1, 1, 0], [0, 1, 0], [1, 0, 1]);

    [Fact]
    public void CountRegions_FourConnected()
    {
        var result = Assert.IsType<RegionsResult>(new CountRegionsOperation().Execute(RegionGrid()).Value);
        Assert.Equal(new RegionsResult(RegionCount: 6, LargestRegionSize: 3), result);
    }

    [Fact]
    public void CountRegions_EightConnected_MergesDiagonalNeighbours()
    {
        var result = Assert.IsType<RegionsResult>(new CountRegionsOperation(diagonals: true).Execute(RegionGrid()).Value);
        Assert.Equal(new RegionsResult(2, 5), result);
    }

    [Fact]
    public void CountRegions_OnlyOneValue()
    {
        var ones = Assert.IsType<RegionsResult>(new CountRegionsOperation(value: 1).Execute(RegionGrid()).Value);
        var zeros = Assert.IsType<RegionsResult>(new CountRegionsOperation(value: 0).Execute(RegionGrid()).Value);

        Assert.Equal(new RegionsResult(3, 3), ones);
        Assert.Equal(new RegionsResult(3, 2), zeros);
    }

    [Fact]
    public void CountRegions_UniformMatrix_IsOneRegion()
    {
        var result = Assert.IsType<RegionsResult>(new CountRegionsOperation().Execute(new Matrix(50, 50)).Value);
        Assert.Equal(new RegionsResult(1, 2_500), result);
    }

    [Fact]
    public void CountRegions_LargestPossibleRegion_DoesNotOverflowTheStack()
    {
        var result = Assert.IsType<RegionsResult>(new CountRegionsOperation().Execute(new Matrix(500, 1_000)).Value);
        Assert.Equal(500_000, result.LargestRegionSize);
    }

    // ---------- grid-shortest-path ----------

    [Fact]
    public void GridPath_OpenGrid_FourVersusEightConnected()
    {
        var open = new Matrix(3, 3);

        var orthogonal = Assert.IsType<GridPathResult>(new GridShortestPathOperation(0, 0, 2, 2).Execute(open).Value);
        var diagonal = Assert.IsType<GridPathResult>(new GridShortestPathOperation(0, 0, 2, 2, diagonals: true).Execute(open).Value);

        Assert.Equal(4, orthogonal.Length);
        Assert.Equal(2, diagonal.Length);
    }

    [Fact]
    public void GridPath_RoutesAroundAWall()
    {
        var grid = MatrixTestData.From([0, 1, 0], [0, 1, 0], [0, 0, 0]);

        var result = Assert.IsType<GridPathResult>(new GridShortestPathOperation(0, 0, 0, 2).Execute(grid).Value);

        Assert.True(result.Found);
        Assert.Equal(6, result.Length);
        Assert.Equal(new[]
        {
            new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(2, 0), new GridPosition(2, 1),
            new GridPosition(2, 2), new GridPosition(1, 2), new GridPosition(0, 2)
        }, result.Path);
    }

    [Fact]
    public void GridPath_NoRoute_IsAResultNotAnError()
    {
        var walled = MatrixTestData.From([0, 1, 0]);

        var result = Assert.IsType<GridPathResult>(new GridShortestPathOperation(0, 0, 0, 2).Execute(walled).Value);

        Assert.False(result.Found);
    }

    [Fact]
    public void GridPath_StartOnAWall_IsNotFound()
    {
        var grid = MatrixTestData.From([1, 0], [0, 0]);

        var result = Assert.IsType<GridPathResult>(new GridShortestPathOperation(0, 0, 1, 1).Execute(grid).Value);

        Assert.False(result.Found);
    }

    [Fact]
    public void GridPath_StartEqualsTarget_IsAZeroLengthPath()
    {
        var result = Assert.IsType<GridPathResult>(new GridShortestPathOperation(1, 1, 1, 1).Execute(new Matrix(3, 3)).Value);

        Assert.True(result.Found);
        Assert.Equal(0, result.Length);
        Assert.Equal(new[] { new GridPosition(1, 1) }, result.Path);
    }

    [Fact]
    public void GridPath_OutOfRangeEndpoint_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new GridShortestPathOperation(0, 0, 9, 9).Execute(new Matrix(3, 3)));
    }

    [Fact]
    public void GridPath_VeryLongPath_ReportsLengthButOmitsThePath()
    {
        var corridor = new Matrix(1, 60_000);

        var result = Assert.IsType<GridPathResult>(
            new GridShortestPathOperation(0, 0, 0, 59_999).Execute(corridor).Value);

        Assert.True(result.Found);
        Assert.Equal(59_999, result.Length);
        Assert.True(result.PathOmitted);
        Assert.Null(result.Path);
    }
}