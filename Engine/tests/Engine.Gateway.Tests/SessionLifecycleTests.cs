using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Engine.Protocol;
using Engine.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Engine.Gateway.Tests;

public class SessionLifecycleTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private SessionManager Sessions => factory.Services.GetRequiredService<SessionManager>();

    [Fact]
    public async Task ClosingConnection_EvictsItsSession()
    {
        int baseline = Sessions.ActiveSessionCount;
        using var socket = await ConnectAsync();
        await CreateMatrixSessionAsync(socket);
        Assert.Equal(baseline + 1, Sessions.ActiveSessionCount);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        await WaitUntil(() => Sessions.ActiveSessionCount == baseline);
    }

    [Fact]
    public async Task CreatingSecondSession_EvictsTheFirst()
    {
        int baseline = Sessions.ActiveSessionCount;
        using var socket = await ConnectAsync();

        var first = await CreateMatrixSessionAsync(socket);
        var second = await CreateMatrixSessionAsync(socket);

        Assert.Equal(baseline + 1, Sessions.ActiveSessionCount);
        Assert.Null(Sessions.TryGet(first));
        Assert.NotNull(Sessions.TryGet(second));
    }

    [Fact]
    public async Task ExecutingAgainstEvictedSession_ReportsSessionExpired()
    {
        using var socket = await ConnectAsync();
        var id = await CreateMatrixSessionAsync(socket);
        Sessions.Evict(id); // simulates what the idle sweeper does

        var requestId = Guid.NewGuid();
        await SendAsync(socket, new ExecuteOperationMessage(requestId, "fill-even-odd", new()));

        var failed = Assert.IsType<OperationFailedMessage>(await ReceiveAsync(socket));
        Assert.Equal(requestId, failed.RequestId);
        Assert.StartsWith("session-expired", failed.Reason);
    }

    private async Task<WebSocket> ConnectAsync()
    {
        var client = factory.Server.CreateWebSocketClient();
        var wsUri = new UriBuilder(factory.Server.BaseAddress) { Scheme = "ws", Path = "/session" }.Uri;
        return await client.ConnectAsync(wsUri, CancellationToken.None);
    }

    private static async Task<Guid> CreateMatrixSessionAsync(WebSocket socket)
    {
        await SendAsync(socket, new CreateSessionMessage("matrix",
            JsonSerializer.SerializeToElement(new { rows = 2, columns = 2 })));
        return Assert.IsType<SessionReadyMessage>(await ReceiveAsync(socket)).SessionId;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.True(condition(), "Condition was not met within 5 seconds");
    }

    private static async Task SendAsync(WebSocket socket, ClientMessage message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, ProtocolJson.Options));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<ServerMessage> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        return JsonSerializer.Deserialize<ServerMessage>(
            Encoding.UTF8.GetString(buffer, 0, result.Count), ProtocolJson.Options)!;
    }
}