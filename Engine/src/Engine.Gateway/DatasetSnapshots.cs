using System.Text.Json;
using Engine.Domain;
using Engine.Domain.Datasets;
using Engine.Protocol;

namespace Engine.Gateway;

/// <summary>
/// Turns a committed dataset into a wire snapshot, or returns null when it shouldn't be sent:
/// types with no snapshot support, and graphs too large to be worth serializing.
/// </summary>
public static class DatasetSnapshots
{
    // 200,000 directed edges is roughly 3 MB of JSON: about a 100,000-node maze, or ~316x316.
    // Checked BEFORE serializing, so the oversized case never builds the big payload at all.
    public const int MaxGraphEdges = 200_000;

    public static DatasetSnapshotDto? TryCreate(IDataset dataset) => dataset switch
    {
        // Matrix size is already bounded by DatasetLimits (500,000 cells).
        Matrix m => new DatasetSnapshotDto("matrix", MatrixPayload(m)),
        Graph g when g.EdgeCount <= MaxGraphEdges => new DatasetSnapshotDto("graph", GraphPayload(g)),
        _ => null
    };

    private static JsonElement MatrixPayload(Matrix m)
    {
        var cells = new int[m.Rows * m.Columns];
        for (int r = 0; r < m.Rows; r++)
            for (int c = 0; c < m.Columns; c++)
                cells[r * m.Columns + c] = m[r, c];

        return JsonSerializer.SerializeToElement(
            new { rows = m.Rows, columns = m.Columns, cells }, ProtocolJson.Options);
    }

    private static JsonElement GraphPayload(Graph g)
    {
        var edges = g.AllDirectedEdges()
            .Select(e => new object[] { e.From, e.To, e.Weight })
            .ToArray();

        return JsonSerializer.SerializeToElement(
            new { nodeCount = g.NodeCount, edges }, ProtocolJson.Options);
    }
}