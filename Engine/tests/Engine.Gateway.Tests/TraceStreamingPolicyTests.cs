using Engine.Domain.Tracing;
using Xunit;

namespace Engine.Gateway.Tests;

public class TraceStreamingPolicyTests
{
    [Fact]
    public void BuildMessages_UnderThreshold_OneMessagePerStep()
    {
        var trace = Enumerable.Range(0, 5)
            .Select(i => new DomainStepEvent("TestStep", $"step {i}"))
            .ToList();

        var messages = TraceStreamingPolicy.BuildMessages(trace).ToList();

        Assert.Equal(5, messages.Count);
        Assert.All(messages, m => Assert.Equal("TestStep", m.StepType));
    }

    [Fact]
    public void BuildMessages_OverThreshold_SingleCoalescedMessage()
    {
        var trace = Enumerable.Range(0, TraceStreamingPolicy.MaxStreamableSteps + 1)
            .Select(i => new DomainStepEvent("TestStep", $"step {i}"))
            .ToList();

        var messages = TraceStreamingPolicy.BuildMessages(trace).ToList();

        var single = Assert.Single(messages);
        Assert.Equal("TraceCoalesced", single.StepType);
    }

    [Fact]
    public void BuildMessages_ExactlyAtThreshold_StillStreamsIndividually()
    {
        var trace = Enumerable.Range(0, TraceStreamingPolicy.MaxStreamableSteps)
            .Select(i => new DomainStepEvent("TestStep", $"step {i}"))
            .ToList();

        var messages = TraceStreamingPolicy.BuildMessages(trace).ToList();

        Assert.Equal(TraceStreamingPolicy.MaxStreamableSteps, messages.Count); // boundary case: pins down <= vs <
    }
    [Fact]
    public void BuildMessages_TruncatedRecorder_ReportsTrueTotalNotRetainedCount()
    {
        var recorder = new TraceRecorder();
        int total = TraceStreamingPolicy.MaxStreamableSteps * 3;
        for (int i = 0; i < total; i++) recorder.Add(new DomainStepEvent("Step", $"step {i}"));

        var message = Assert.Single(TraceStreamingPolicy.BuildMessages(recorder));

        Assert.Equal("TraceCoalesced", message.StepType);
        Assert.Equal(total, message.Payload!.Value.GetProperty("totalSteps").GetInt32());
    }
}