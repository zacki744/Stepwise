using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Engine.Gateway.Tests;

public class OperationLimitEndpointTests
{
    [Fact]
    public async Task OperationExceedingWorkBudget_FailsCleanly_StateUntouched_ConnectionSurvives()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<ExecutionLimitsOptions>(o => o.MaxWorkUnits = 500)));
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        // A 100x100 matrix with every cell set to 9, so a 1x1 item has nowhere to go
        // and must scan all 10,000 positions.
        var cells = Enumerable.Repeat(9, 100 * 100).ToArray();
        await GatewayTestHelpers.SendAsync(socket, new CreateSessionMessage("matrix",
            JsonSerializer.SerializeToElement(new { rows = 100, columns = 100, cells })));
        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        var requestId = Guid.NewGuid();
        await GatewayTestHelpers.SendAsync(socket, new ExecuteOperationMessage(requestId, "place-items",
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(new[] { new { id = 1, width = 1, height = 1 } })
            }));

        var failed = Assert.IsType<OperationFailedMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal(requestId, failed.RequestId);
        Assert.StartsWith("operation-aborted", failed.Reason);

        // Same connection still works, and the aborted run changed nothing.
        await GatewayTestHelpers.SendAsync(socket, new ExecuteOperationMessage(Guid.NewGuid(), "read-cell",
            new Dictionary<string, JsonElement>
            {
                ["row"] = JsonSerializer.SerializeToElement(0),
                ["column"] = JsonSerializer.SerializeToElement(0)
            }));
        var ok = Assert.IsType<OperationCompleteMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal(9, ok.ResultSummary!.Value.GetInt32());
    }
}