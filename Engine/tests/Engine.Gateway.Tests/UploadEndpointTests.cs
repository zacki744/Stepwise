using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Engine.Gateway.Tests;

public class UploadEndpointTests
{
    // 700 x 700 = 490,000 cells (under the dataset limit) of 5-digit values: about 2.9 MB of JSON.
    private const int Size = 700;

    private static string BigCreateMatrixJson()
    {
        var cells = Enumerable.Range(0, Size * Size).Select(i => i % 90_000 + 10_000).ToArray();
        var payload = JsonSerializer.SerializeToElement(new { rows = Size, columns = Size, cells });
        return JsonSerializer.Serialize<ClientMessage>(new CreateSessionMessage("matrix", payload), ProtocolJson.Options);
    }

    private static async Task SendInChunksAsync(System.Net.WebSockets.WebSocket socket, string json, int chunkChars = 200_000)
    {
        var id = Guid.NewGuid();
        int total = (json.Length + chunkChars - 1) / chunkChars;
        for (int i = 0; i < total; i++)
        {
            var piece = json.Substring(i * chunkChars, Math.Min(chunkChars, json.Length - i * chunkChars));
            await GatewayTestHelpers.SendAsync(socket, new UploadChunkMessage(id, i, total, piece));
        }
    }

    private static async Task<OperationCompleteMessage> AwaitCompleteAsync(System.Net.WebSockets.WebSocket socket)
    {
        while (true)
        {
            var message = await GatewayTestHelpers.ReceiveAsync(socket);
            if (message is OperationCompleteMessage complete) return complete;
            Assert.IsNotType<OperationFailedMessage>(message);
            Assert.IsNotType<ErrorMessage>(message);
        }
    }

    [Fact]
    public async Task SameMessageSentInOneFrame_IsTooLarge()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendRawAsync(socket, BigCreateMatrixJson());

        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal("message-too-large", error.Code);
    }

    [Fact]
    public async Task LargeCreateSession_SentInChunks_BuildsTheDataset()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await SendInChunksAsync(socket, BigCreateMatrixJson());
        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        await GatewayTestHelpers.SendAsync(socket, new ExecuteOperationMessage(
            Guid.NewGuid(), "read-cell", new Dictionary<string, JsonElement>
            {
                ["row"] = JsonSerializer.SerializeToElement(3),
                ["column"] = JsonSerializer.SerializeToElement(4)
            }));

        var complete = await AwaitCompleteAsync(socket);
        Assert.Equal(3 * Size + 4 + 10_000, complete.ResultSummary!.Value.GetInt32());
    }

    [Fact]
    public async Task RehydrateSession_CanBeUploadedInChunks()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        var payload = JsonSerializer.SerializeToElement(new { rows = 2, columns = 2 });
        var json = JsonSerializer.Serialize<ClientMessage>(
            new RehydrateSessionMessage("matrix", payload, []), ProtocolJson.Options);
        await SendInChunksAsync(socket, json, chunkChars: 20);

        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
    }

    [Fact]
    public async Task OutOfOrderChunk_IsRejected_AndTheConnectionStaysUsable()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        var id = Guid.NewGuid();

        await GatewayTestHelpers.SendAsync(socket, new UploadChunkMessage(id, 0, 3, "{"));
        await GatewayTestHelpers.SendAsync(socket, new UploadChunkMessage(id, 2, 3, "}")); // chunk 1 skipped

        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal("upload-out-of-order", error.Code);

        await GatewayTestHelpers.SendCreateMatrixAsync(socket, 2, 2);
        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
    }

    [Fact]
    public async Task UploadCarryingAnotherMessageType_IsRejected()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        var inner = JsonSerializer.Serialize<ClientMessage>(
            new ExecuteOperationMessage(Guid.NewGuid(), "transpose", new Dictionary<string, JsonElement>()),
            ProtocolJson.Options);
        await SendInChunksAsync(socket, inner, chunkChars: 30);

        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal("invalid-upload", error.Code);
    }

    [Fact]
    public async Task UploadBeyondTheConfiguredLimit_IsRejected()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionLimits:MaxUploadBytes"] = "100" })));
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await SendInChunksAsync(socket, new string('x', 400), chunkChars: 150);  // the first chunk alone is already over 100 bytes

        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal("upload-too-large", error.Code);
    }

    [Fact]
    public async Task AbandonedUpload_ReleasesItsBufferedBytes_WhenTheConnectionCloses()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var budget = (UploadBudget)factory.Services.GetService(typeof(UploadBudget))!;

        using (var socket = await GatewayTestHelpers.ConnectAsync(factory))
        {
            await GatewayTestHelpers.SendAsync(socket, new UploadChunkMessage(Guid.NewGuid(), 0, 5, new string('a', 1_000)));
            await WaitUntilAsync(() => budget.InFlightBytes == 1_000 * UploadAssembler.MemoryFactor);

            await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }

        await WaitUntilAsync(() => budget.InFlightBytes == 0);
        Assert.Equal(0, budget.InFlightBytes);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
    }
}
