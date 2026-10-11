using System.Text.Json;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Gateway.Tests;

public class DatasetSnapshotTests
{
    [Fact]
    public void Matrix_RoundTripsThroughFactory()
    {
        var original = new Matrix(2, 3);
        original[0, 1] = 5;
        original[1, 2] = -4;

        var snapshot = DatasetSnapshots.TryCreate(original)!;
        var rebuilt = Assert.IsType<Matrix>(DatasetFactory.Create(snapshot.DatasetType, snapshot.Payload));

        Assert.Equal("matrix", snapshot.DatasetType);
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 3; c++)
                Assert.Equal(original[r, c], rebuilt[r, c]);
    }

    [Fact]
    public void Graph_RoundTripsThroughFactory_IncludingWeights()
    {
        var original = new Graph(4, [(0, 1, 2.5), (1, 2, 1.0), (3, 0, 7.0)]);

        var snapshot = DatasetSnapshots.TryCreate(original)!;
        var rebuilt = Assert.IsType<Graph>(DatasetFactory.Create(snapshot.DatasetType, snapshot.Payload));

        Assert.Equal("graph", snapshot.DatasetType);
        Assert.Equal(original.NodeCount, rebuilt.NodeCount);
        Assert.Equal(original.AllDirectedEdges().ToList(), rebuilt.AllDirectedEdges().ToList());
    }

    [Fact]
    public void Graph_OverEdgeLimit_ReturnsNull()
    {
        var edges = Enumerable.Range(0, DatasetSnapshots.MaxGraphEdges + 1)
            .Select(i => (From: i % 500, To: (i + 1) % 500, Weight: 1.0));
        var big = new Graph(500, edges);

        Assert.Null(DatasetSnapshots.TryCreate(big));
    }

    [Fact]
    public void Tree_RoundTripsThroughFactory_IncludingDeadSlots()
    {
        var original = Tree.FromValues(new[] { 50, 30, 70, 20, 40 });
        original = (Tree)new RemoveNodeOperation(20).Execute(original).NewState!; // leaves slot 3 dead

        var snapshot = DatasetSnapshots.TryCreate(original)!;
        var rebuilt = Assert.IsType<Tree>(DatasetFactory.Create(snapshot.DatasetType, snapshot.Payload));

        Assert.Equal("tree", snapshot.DatasetType);
        Assert.Equal(original.SlotCount, rebuilt.SlotCount);
        Assert.Equal(original.LiveNodes(), rebuilt.LiveNodes());
    }

    [Fact]
    public void Tree_Empty_RoundTrips()
    {
        var snapshot = DatasetSnapshots.TryCreate(Tree.Empty())!;
        var rebuilt = Assert.IsType<Tree>(DatasetFactory.Create(snapshot.DatasetType, snapshot.Payload));

        Assert.Null(rebuilt.RootIndex);
        Assert.Equal(0, rebuilt.SlotCount);
    }

    [Fact]
    public void Tree_PayloadShape_IsRootSlotCountAndNodeArrays()
    {
        var snapshot = DatasetSnapshots.TryCreate(Tree.FromValues(new[] { 5, 3, 8 }))!;

        Assert.Equal("""{"root":0,"slotCount":3,"nodes":[[0,5,1,2],[1,3,-1,-1],[2,8,-1,-1]]}""",
            snapshot.Payload.GetRawText());
    }

    [Fact]
    public void Inline_IsOnlyForSmallDatasets()
    {
        Assert.NotNull(DatasetSnapshots.TryCreateInline(new Matrix(100, 200)));  // exactly 20,000 cells
        Assert.Null(DatasetSnapshots.TryCreateInline(new Matrix(100, 201)));
        Assert.NotNull(DatasetSnapshots.TryCreate(new Matrix(100, 201)));         // still available in full
        Assert.NotNull(DatasetSnapshots.TryCreateInline(Tree.FromValues(new[] { 1, 2, 3 })));
        Assert.NotNull(DatasetSnapshots.TryCreateInline(Graph.Empty(10)));
    }

    [Fact]
    public void Factory_TreeFromValues_InsertsInOrder()
    {
        var payload = JsonSerializer.SerializeToElement(new { values = new[] { 5, 3, 8 } });

        var tree = Assert.IsType<Tree>(DatasetFactory.Create("tree", payload));

        Assert.Equal(new[] { 3, 5, 8 }, tree.InOrder().Select(tree.GetValue));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"rows":3}""")]
    public void Factory_TreeWithoutValuesOrNodes_IsEmpty(string json)
    {
        var tree = Assert.IsType<Tree>(DatasetFactory.Create("tree", JsonDocument.Parse(json).RootElement));
        Assert.Null(tree.RootIndex);
    }

    [Theory]
    [InlineData("""{"values":"nope"}""")]
    [InlineData("""{"values":[1,"x"]}""")]
    [InlineData("""{"nodes":[[0,5,-1,-1]]}""")]                      // no root
    [InlineData("""{"root":0,"nodes":[[0,5,-1]]}""")]                // short node
    [InlineData("""{"root":0,"nodes":[[0,5,1,-1]]}""")]              // child that doesn't exist
    [InlineData("""{"root":0,"nodes":[[0,5,1,-1],[1,9,-1,-1]]}""")]  // left child larger than parent
    public void Factory_InvalidTreePayload_IsAClientInputError(string json)
    {
        var ex = Record.Exception(() => DatasetFactory.Create("tree", JsonDocument.Parse(json).RootElement));

        Assert.NotNull(ex);
        Assert.True(ex is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException,
            $"unexpected {ex.GetType().Name}: {ex.Message}");
    }

    [Fact]
    public void Factory_MatrixCellsWithWrongLength_Throws()
    {
        var payload = JsonSerializer.SerializeToElement(new { rows = 2, columns = 2, cells = new[] { 1, 2, 3 } });

        Assert.Throws<ArgumentException>(() => DatasetFactory.Create("matrix", payload));
    }
}