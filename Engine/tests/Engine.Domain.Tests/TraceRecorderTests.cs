using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Domain.Tracing;
using Xunit;

namespace Engine.Domain.Tests;

public class TraceRecorderTests
{
    private static DomainStepEvent Step(string type, int i) => new(type, $"step {i}");

    [Fact]
    public void Add_WithinCap_RetainsEverything()
    {
        var recorder = new TraceRecorder(maxEvents: 5);
        for (int i = 0; i < 5; i++) recorder.Add(Step("A", i));

        Assert.Equal(5, recorder.Count);
        Assert.Equal(5, recorder.TotalCount);
        Assert.False(recorder.IsTruncated);
    }

    [Fact]
    public void Add_BeyondCap_KeepsFirstEventsInOrder_ButCountsAll()
    {
        var recorder = new TraceRecorder(maxEvents: 3);
        for (int i = 0; i < 10; i++) recorder.Add(Step("A", i));

        Assert.Equal(3, recorder.Count);
        Assert.Equal(10, recorder.TotalCount);
        Assert.True(recorder.IsTruncated);
        Assert.Equal(new[] { "step 0", "step 1", "step 2" }, recorder.Select(e => e.Description).ToArray());
    }

    [Fact]
    public void Summary_CountsEveryEventByType_IncludingDroppedOnes()
    {
        var recorder = new TraceRecorder(maxEvents: 2);
        for (int i = 0; i < 4; i++) recorder.Add(Step("A", i));
        for (int i = 0; i < 3; i++) recorder.Add(Step("B", i));

        var summary = recorder.Summary;

        Assert.Equal(7, summary.TotalCount);
        Assert.Equal(4, summary.CountsByType["A"]);
        Assert.Equal(3, summary.CountsByType["B"]);
    }

    [Fact]
    public void TraceSummary_Of_PlainList_CountsFromTheList()
    {
        var list = new List<DomainStepEvent> { Step("A", 0), Step("A", 1), Step("B", 2) };

        var summary = TraceSummary.Of(list);

        Assert.Equal(3, summary.TotalCount);
        Assert.Equal(2, summary.CountsByType["A"]);
    }

    [Fact]
    public void GenerateMaze_LargeMaze_RetainsBoundedTrace_ButCountsEveryPassage()
    {
        var result = new GenerateMazeOperation(width: 100, height: 100, seed: 1).Execute(Graph.Empty(1));

        Assert.Equal(TraceRecorder.DefaultMaxEvents, result.Trace.Count); // memory is bounded
        var summary = TraceSummary.Of(result.Trace);
        Assert.Equal(9_999, summary.TotalCount);                          // accounting is not
        Assert.Equal(9_999, summary.CountsByType["PassageCarved"]);
    }
}