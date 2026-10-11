using System.Text.Json;
using Engine.Domain.Datasets;
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
    public void Tree_HasNoSnapshotSupport_ReturnsNull()
    {
        Assert.Null(DatasetSnapshots.TryCreate(Tree.Empty()));
    }

    [Fact]
    public void Factory_MatrixCellsWithWrongLength_Throws()
    {
        var payload = JsonSerializer.SerializeToElement(new { rows = 2, columns = 2, cells = new[] { 1, 2, 3 } });

        Assert.Throws<ArgumentException>(() => DatasetFactory.Create("matrix", payload));
    }
}