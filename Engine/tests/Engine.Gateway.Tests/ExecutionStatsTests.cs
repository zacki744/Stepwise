using System.Net.WebSockets;
using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Engine.Gateway.Tests;

public class ExecutionStatsTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task<(List<StepEventMessage> Steps, OperationCompleteMessage Complete)> RunAsync(
        WebSocket socket, string operationId, Dictionary<string, JsonElement> options)
    {
        await GatewayTestHelpers.SendAsync(socket, new ExecuteOperationMessage(Guid.NewGuid(), operationId, options));
        var steps = new List<StepEventMessage>();
        while (true)
        {
            var message = await GatewayTestHelpers.ReceiveAsync(socket);
            if (message is StepEventMessage step) steps.Add(step);
            else if (message is OperationCompleteMessage complete) return (steps, complete);
            else Assert.Fail($"unexpected {message.GetType().Name}");
        }
    }

    [Fact]
    public async Task Complete_CarriesStats_MatchingTheStreamedSteps()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.CreateMatrixSessionAsync(socket);

        var (steps, complete) = await RunAsync(socket, "fill-even-odd", new());

        var stats = Assert.IsType<ExecutionStatsDto>(complete.Stats);
        Assert.Equal(steps.Count, stats.Steps);
        Assert.True(stats.Steps > 0);
        Assert.True(stats.WorkUnits >= stats.Steps);
        Assert.True(stats.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Stats_CountTheRealStepsEvenWhenTheTraceIsCoalesced()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.SendCreateMatrixAsync(socket, 100, 100);
        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        var (steps, complete) = await RunAsync(socket, "fill-even-odd", new()); // 10,000 cells, one step each

        var step = Assert.Single(steps);
        Assert.Equal("TraceCoalesced", step.StepType);
        Assert.Equal(10_000, Assert.IsType<ExecutionStatsDto>(complete.Stats).Steps);
    }

    [Fact]
    public async Task QueryWithoutTrace_StillReportsStats()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.CreateMatrixSessionAsync(socket);

        var (_, complete) = await RunAsync(socket, "read-cell", new() { ["row"] = Json(0), ["column"] = Json(0) });

        Assert.NotNull(complete.Stats);
        Assert.True(complete.Stats!.ElapsedMs >= 0);
    }
}
