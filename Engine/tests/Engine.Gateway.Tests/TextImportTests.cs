using System.Text.Json;
using Engine.Domain.Datasets;
using Xunit;

namespace Engine.Gateway.Tests;

public class TextImportTests
{
    private static List<(int From, int To, double Weight)> Edges(Graph g) =>
        g.AllDirectedEdges().OrderBy(e => e.From).ThenBy(e => e.To).ToList();

    private static TextImportException LineError(Action action) => Assert.Throws<TextImportException>(action);

    // ---------- matrix ----------

    [Fact]
    public void Matrix_ParsesRows_WithSpacesCommasTabsBlankLinesAndComments()
    {
        var matrix = TextImport.ParseMatrix("# a grid\n1 2, 3\n\n4\t5 6  # trailing\n");

        Assert.Equal(2, matrix.Rows);
        Assert.Equal(3, matrix.Columns);
        Assert.Equal(1, matrix[0, 0]);
        Assert.Equal(3, matrix[0, 2]);
        Assert.Equal(6, matrix[1, 2]);
    }

    [Fact]
    public void Matrix_AcceptsWindowsLineEndings_AndNegativeNumbers()
    {
        var matrix = TextImport.ParseMatrix("1 -2\r\n3 4\r\n");

        Assert.Equal(-2, matrix[0, 1]);
        Assert.Equal(2, matrix.Rows);
    }

    [Fact]
    public void Matrix_RaggedRow_NamesTheLine()
    {
        var error = LineError(() => TextImport.ParseMatrix("1 2 3\n4 5\n6 7 8"));

        Assert.Equal(2, error.Line);
        Assert.StartsWith("line 2:", error.Message);
        Assert.Contains("expected 3 values", error.Message);
    }

    [Fact]
    public void Matrix_NotANumber_NamesTheLineAndTheToken()
    {
        var error = LineError(() => TextImport.ParseMatrix("1 2\n3 x"));

        Assert.Equal(2, error.Line);
        Assert.Contains("'x'", error.Message);
    }

    [Fact]
    public void Matrix_LineNumbersCountBlankAndCommentLines()
    {
        var error = LineError(() => TextImport.ParseMatrix("# header\n\n1 2\n3 x"));

        Assert.Equal(4, error.Line);
    }

    [Fact]
    public void Matrix_DecimalOrHugeNumbers_AreRejected()
    {
        Assert.Equal(1, LineError(() => TextImport.ParseMatrix("1.5 2")).Line);
        Assert.Equal(1, LineError(() => TextImport.ParseMatrix("99999999999 2")).Line);
    }

    [Fact]
    public void Matrix_Empty_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => TextImport.ParseMatrix("  \n# nothing\n"));
    }

    [Fact]
    public void Matrix_BeyondTheCellLimit_StopsAtTheLineThatCrossesIt()
    {
        var row = string.Join(' ', Enumerable.Repeat("0", 1000));
        var text = string.Join('\n', Enumerable.Repeat(row, 600));

        var error = LineError(() => TextImport.ParseMatrix(text));

        Assert.Equal(501, error.Line); // 500 rows of 1,000 is exactly the 500,000-cell limit
    }

    // ---------- graph: edge list ----------

    [Fact]
    public void EdgeList_Undirected_StoresBothDirections_AndFindsTheNodeCount()
    {
        var graph = TextImport.ParseGraph("0 1\n1 2 4.5", "edgeList", directed: false, nodeCount: null);

        Assert.Equal(3, graph.NodeCount);
        Assert.Equal([(0, 1, 1.0), (1, 0, 1.0), (1, 2, 4.5), (2, 1, 4.5)], Edges(graph));
    }

    [Fact]
    public void EdgeList_Directed_KeepsEachLineAsOneEdge_IncludingParallelOnes()
    {
        var graph = TextImport.ParseGraph("0 1\n0 1 2\n1 0", "edgeList", directed: true, nodeCount: null);

        Assert.Equal(3, graph.EdgeCount);
        Assert.Equal([(0, 1, 1.0), (0, 1, 2.0), (1, 0, 1.0)], Edges(graph));
    }

    [Fact]
    public void EdgeList_Undirected_SamePairFromTheOtherEnd_IsOneConnection()
    {
        var graph = TextImport.ParseGraph("0 1 3\n1 0 3", "edgeList", false, null);

        Assert.Equal(2, graph.EdgeCount);
    }

    [Fact]
    public void EdgeList_Undirected_ConflictingWeights_ArePointedOut()
    {
        var error = LineError(() => TextImport.ParseGraph("0 1 3\n2 3\n1 0 4", "edgeList", false, null));

        Assert.Equal(3, error.Line);
        Assert.Contains("line 1", error.Message);
        Assert.Contains("weight 3", error.Message);
    }

    [Fact]
    public void EdgeList_Undirected_SelfLoop_IsStoredOnce()
    {
        var graph = TextImport.ParseGraph("1 1", "edgeList", false, null);

        Assert.Equal(2, graph.NodeCount);
        Assert.Equal(1, graph.EdgeCount);
    }

    [Fact]
    public void EdgeList_NodeCount_AddsIsolatedNodes_AndMustCoverTheText()
    {
        var graph = TextImport.ParseGraph("0 1", "edgeList", false, nodeCount: 5);
        Assert.Equal(5, graph.NodeCount);

        Assert.Throws<ArgumentException>(() => TextImport.ParseGraph("0 4", "edgeList", false, nodeCount: 3));
    }

    [Fact]
    public void EdgeList_NodeCountAloneGivesAnEdgelessGraph()
    {
        var graph = TextImport.ParseGraph("# no edges yet", "edgeList", false, nodeCount: 4);

        Assert.Equal(4, graph.NodeCount);
        Assert.Equal(0, graph.EdgeCount);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("0 1 2 3", 1)]
    [InlineData("0 -1", 1)]
    [InlineData("0 x", 1)]
    [InlineData("0 1 NaN", 1)]
    [InlineData("0 1 Infinity", 1)]
    [InlineData("0 1\n2", 2)]
    public void EdgeList_BadLines_NameTheLine(string text, int expectedLine)
    {
        Assert.Equal(expectedLine, LineError(() => TextImport.ParseGraph(text, "edgeList", false, null)).Line);
    }

    [Fact]
    public void EdgeList_Empty_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => TextImport.ParseGraph("\n# nothing", "edgeList", false, null));
    }

    [Fact]
    public void EdgeList_AbsurdNodeNumber_IsRejectedWithoutAllocating()
    {
        Assert.Throws<ArgumentException>(() => TextImport.ParseGraph("0 2000000000", "edgeList", false, null));
    }

    // ---------- graph: adjacency list ----------

    [Fact]
    public void AdjacencyList_Undirected_MergesTheTwoListingsOfEachConnection()
    {
        var graph = TextImport.ParseGraph("0: 1 2\n1: 0\n2: 0\n3:", "adjacencyList", false, null);

        Assert.Equal(4, graph.NodeCount); // node 3 only appears as "3:"
        Assert.Equal([(0, 1, 1.0), (0, 2, 1.0), (1, 0, 1.0), (2, 0, 1.0)], Edges(graph));
    }

    [Fact]
    public void AdjacencyList_Directed_KeepsEachListedNeighbourAsOneEdge()
    {
        var graph = TextImport.ParseGraph("0: 1, 2\n1: 2", "adjacencyList", true, null);

        Assert.Equal([(0, 1, 1.0), (0, 2, 1.0), (1, 2, 1.0)], Edges(graph));
    }

    [Theory]
    [InlineData("0 1 2", 1)]
    [InlineData("0: 1\n1 0", 2)]
    [InlineData("0: 1: 2", 1)]
    [InlineData(": 1", 1)]
    [InlineData("0 1: 2", 1)]
    [InlineData("0: x", 1)]
    public void AdjacencyList_BadLines_NameTheLine(string text, int expectedLine)
    {
        Assert.Equal(expectedLine, LineError(() => TextImport.ParseGraph(text, "adjacencyList", false, null)).Line);
    }

    [Fact]
    public void UnknownGraphFormat_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => TextImport.ParseGraph("0 1", "dot", false, null));
    }

    // ---------- tree ----------

    [Fact]
    public void Tree_InsertsTheValuesInOrder_AcrossLines()
    {
        var parsed = TextImport.ParseTree("50 30, 70\n# more\n20 40");
        var expected = Tree.FromValues([50, 30, 70, 20, 40]);

        Assert.Equal(expected.LiveNodes(), parsed.LiveNodes());
    }

    [Fact]
    public void Tree_Empty_IsAnEmptyTree()
    {
        Assert.Empty(TextImport.ParseTree("# nothing").LiveNodes());
    }

    [Fact]
    public void Tree_BadValue_NamesTheLine()
    {
        var error = LineError(() => TextImport.ParseTree("5 3\n8 oops"));

        Assert.Equal(2, error.Line);
    }

    // ---------- through the factory ----------

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Factory_BuildsEachTypeFromText()
    {
        Assert.IsType<Matrix>(DatasetFactory.Create("matrix", Json(new { text = "1 2\n3 4" })));
        Assert.IsType<Tree>(DatasetFactory.Create("tree", Json(new { text = "5 3 8" })));

        var graph = Assert.IsType<Graph>(DatasetFactory.Create("graph",
            Json(new { text = "0: 1\n1: 2", format = "adjacencyList", directed = true })));
        Assert.Equal(3, graph.NodeCount);
        Assert.Equal(2, graph.EdgeCount);
    }

    [Fact]
    public void Factory_GraphDefaultsToAnUndirectedEdgeList()
    {
        var graph = Assert.IsType<Graph>(DatasetFactory.Create("graph", Json(new { text = "0 1" })));

        Assert.Equal(2, graph.EdgeCount);
    }

    [Theory]
    [InlineData("matrix", """{"text":"1 2","rows":1}""")]
    [InlineData("matrix", """{"text":"1 2","cells":[1,2]}""")]
    [InlineData("graph", """{"text":"0 1","edges":[]}""")]
    [InlineData("tree", """{"text":"1 2","values":[3]}""")]
    public void Factory_TextTogetherWithTheJsonShape_IsRejected(string datasetType, string payload)
    {
        using var document = JsonDocument.Parse(payload);

        Assert.Throws<ArgumentException>(() => DatasetFactory.Create(datasetType, document.RootElement));
    }

    [Theory]
    [InlineData("matrix", """{"text":5}""")]
    [InlineData("graph", """{"text":"0 1","format":3}""")]
    [InlineData("graph", """{"text":null}""")]
    public void Factory_WrongTypesForTextFields_AreClientErrors(string datasetType, string payload)
    {
        using var document = JsonDocument.Parse(payload);

        var error = Record.Exception(() => DatasetFactory.Create(datasetType, document.RootElement));

        Assert.True(error is ArgumentException or InvalidOperationException or FormatException, error?.GetType().Name);
    }
}
