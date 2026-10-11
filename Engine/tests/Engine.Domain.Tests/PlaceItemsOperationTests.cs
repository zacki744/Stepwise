using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class PlaceItemsOperationTests
{
    [Fact]
    public void Execute_ItemFits_MarksCorrectCellsWithItemId()
    {
        var op = new PlaceItemsOperation([new BinItem(Id: 1, Width: 2, Height: 2)]);
        var grid = new Matrix(rows: 3, columns: 3);

        var result = op.Execute(grid);

        var placed = Assert.IsType<Matrix>(result.NewState);
        Assert.Equal(1, placed[0, 0]);
        Assert.Equal(1, placed[1, 1]);
        Assert.Equal(0, placed[2, 2]); // outside the 2x2 footprint, still empty
    }

    [Fact]
    public void Execute_ItemTooLargeForGrid_ReportedAsUnplaced()
    {
        var op = new PlaceItemsOperation([new BinItem(Id: 1, Width: 5, Height: 5)]);
        var grid = new Matrix(rows: 3, columns: 3);

        var result = op.Execute(grid);

        var unplaced = Assert.IsType<List<int>>(result.Value);
        Assert.Contains(1, unplaced);
    }

    [Fact]
    public void Execute_MultipleItems_PlacedWithoutOverlap()
    {
        var op = new PlaceItemsOperation([
            new BinItem(Id: 1, Width: 2, Height: 2), // fills the entire 2x2 grid
        new BinItem(Id: 2, Width: 1, Height: 1)  // no room left
        ]);
        var grid = new Matrix(rows: 2, columns: 2);

        var result = op.Execute(grid);

        var placed = Assert.IsType<Matrix>(result.NewState);
        Assert.Equal(1, placed[0, 0]); // item 1 occupies the whole grid
        Assert.Equal(1, placed[1, 1]);

        var unplaced = Assert.IsType<List<int>>(result.Value);
        Assert.Single(unplaced);
        Assert.Equal(2, unplaced[0]); // item 2 genuinely has nowhere to go
    }

    [Fact]
    public void Execute_DoesNotMutateInputGrid()
    {
        var op = new PlaceItemsOperation([new BinItem(Id: 1, Width: 1, Height: 1)]);
        var grid = new Matrix(rows: 2, columns: 2);

        op.Execute(grid);

        Assert.Equal(0, grid[0, 0]); // input untouched — same isolation guarantee as FillMatrixOperation
    }

    [Fact]
    public void Execute_PreservesAlreadyOccupiedCells()
    {
        var grid = new Matrix(rows: 2, columns: 2);
        grid[0, 0] = 99; // simulate a previously placed item

        var op = new PlaceItemsOperation([new BinItem(Id: 1, Width: 1, Height: 1)]);
        var result = op.Execute(grid);

        var placed = Assert.IsType<Matrix>(result.NewState);
        Assert.Equal(99, placed[0, 0]); // untouched by this operation
        Assert.Equal(1, placed[0, 1]);  // new item placed in the first free cell instead
    }
}