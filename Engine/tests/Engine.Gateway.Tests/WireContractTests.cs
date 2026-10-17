using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Domain.Datasets;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Engine.Gateway.Tests;

/// <summary>
/// Pins the JSON shapes the gateway puts inside messages and endpoints: dataset snapshots and the
/// GET /operations and /health bodies. A client reads these directly, so changing one is a breaking change.
/// </summary>
public class WireContractTests
{
    private static void AssertJson(string expected, JsonElement actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual.GetRawText())),
            $"Wire format changed.\nExpected: {expected}\nActual:   {actual.GetRawText()}");

    private static JsonElement Snapshot(Engine.Domain.IDataset dataset)
    {
        var snapshot = DatasetSnapshots.TryCreate(dataset)!;
        return System.Text.Json.JsonSerializer.SerializeToElement(snapshot, Engine.Protocol.ProtocolJson.Options);
    }

    // ---------- dataset snapshots ----------

    [Fact]
    public void MatrixSnapshot()
    {
        var matrix = new Matrix(1, 2);
        matrix[0, 0] = 3;
        matrix[0, 1] = -4;

        AssertJson("""{"datasetType":"matrix","payload":{"rows":1,"columns":2,"cells":[3,-4]}}""", Snapshot(matrix));
    }

    [Fact]
    public void GraphSnapshot_ListsEveryStoredDirectedEdge()
    {
        var graph = new Graph(3, new (int, int, double)[] { (0, 1, 2.5), (1, 0, 2.5) });

        AssertJson("""{"datasetType":"graph","payload":{"nodeCount":3,"edges":[[0,1,2.5],[1,0,2.5]]}}""", Snapshot(graph));
    }

    [Fact]
    public void TreeSnapshot_UsesMinusOneForNoChild_AndSlotOrder()
    {
        var tree = Tree.FromValues([5, 3, 8]);

        AssertJson(
            """{"datasetType":"tree","payload":{"root":0,"slotCount":3,"nodes":[[0,5,1,2],[1,3,-1,-1],[2,8,-1,-1]]}}""",
            Snapshot(tree));
    }

    [Fact]
    public void EmptyTreeSnapshot()
    {
        AssertJson("""{"datasetType":"tree","payload":{"root":-1,"slotCount":0,"nodes":[]}}""", Snapshot(Tree.Empty()));
    }

    // ---------- endpoints ----------

    [Fact]
    public async Task Health_HasExactlyTheDocumentedFields()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var document = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/health"));

        Assert.Equal(
            new[] { "datasetBytes", "maxDatasetBytes", "maxSessions", "sessions", "status" },
            document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task OperationsEntry_ForAnOptionalChoice_HasTheDocumentedShape()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var document = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/operations"));
        var flip = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "flip");

        AssertJson(
            """
            {"id":"flip","datasetType":"matrix","label":"Flip","category":"Reshape",
             "description":"Mirror the grid left-to-right or top-to-bottom.",
             "effect":"replacesState",
             "options":[{"name":"axis","kind":"choice","label":"Axis","required":false,"default":"horizontal","choices":["horizontal","vertical"]}],
             "returns":null,
             "steps":[{"type":"MatrixReshaped","description":"The grid was mirrored.",
                       "visual":{"kind":"none","bind":{}},
                       "payload":[{"name":"fromRows","type":"int","nullable":false},
                                  {"name":"fromColumns","type":"int","nullable":false},
                                  {"name":"toRows","type":"int","nullable":false},
                                  {"name":"toColumns","type":"int","nullable":false}]}],
             "exampleOptions":{"axis":"vertical"}}
            """,
            flip);
    }

    [Fact]
    public async Task OperationsEntry_ForRequiredOptions_HasNullDefaultsAndChoices()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var document = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/operations"));
        var setCell = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "set-cell");

        AssertJson(
            """
            {"id":"set-cell","datasetType":"matrix","label":"Set cell","category":"Edit",
             "description":"Write one value into one cell.",
             "options":[
               {"name":"row","kind":"row","label":"Row","required":true,"default":null,"choices":null},
               {"name":"column","kind":"column","label":"Column","required":true,"default":null,"choices":null},
               {"name":"value","kind":"int","label":"Value","required":true,"default":null,"choices":null}],
             "effect":"replacesStateAndProducesValue",
             "returns":{"description":"The value the cell held before.","value":{"type":"int","nullable":false}},
             "steps":[{"type":"CellChanged","description":"A cell was overwritten.",
                       "visual":{"kind":"highlightCell","bind":{"row":"row","column":"column"}},
                       "payload":[{"name":"row","type":"int","nullable":false},
                                  {"name":"column","type":"int","nullable":false},
                                  {"name":"oldValue","type":"int","nullable":false},
                                  {"name":"newValue","type":"int","nullable":false}]}],
             "exampleOptions":{"row":0,"column":0,"value":7}}
            """,
            setCell);
    }

    [Fact]
    public async Task EveryOperationEntry_HasTheSameTopLevelFields()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var document = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/operations"));

        foreach (var entry in document.RootElement.EnumerateArray())
            Assert.Equal(
                new[] { "category", "datasetType", "description", "effect", "exampleOptions", "id", "label", "options", "returns", "steps" },
                entry.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }
}
