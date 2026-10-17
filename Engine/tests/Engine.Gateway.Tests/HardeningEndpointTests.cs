using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Engine.Gateway.Tests;

public class HardeningEndpointTests
{
    private static WebApplicationFactory<Program> WithOptions(
        Action<SecurityOptions>? security = null, Action<ConnectionLimitsOptions>? limits = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            if (security is not null) services.Configure(security);
            if (limits is not null) services.Configure(limits);
        }));

    private static Task<WebSocket> ConnectWithOriginAsync(WebApplicationFactory<Program> factory, string origin)
    {
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["Origin"] = origin;
        var uri = new UriBuilder(factory.Server.BaseAddress) { Scheme = "ws", Path = "/session" }.Uri;
        return client.ConnectAsync(uri, CancellationToken.None);
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    // ---------- /health ----------

    [Fact]
    public async Task Health_ReportsOkAndSessionCounts()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.CreateMatrixSessionAsync(socket);

        using var document = JsonDocument.Parse(await client.GetStringAsync("/health"));

        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("sessions").GetInt32());
        Assert.True(document.RootElement.GetProperty("maxSessions").GetInt32() > 0);
    }

    // ---------- Origin ----------

    [Fact]
    public async Task WebSocket_FromAnOriginOutsideTheList_IsRefused()
    {
        await using var factory = WithOptions(security: o => o.AllowedOrigins = ["https://app.example"]);

        await Assert.ThrowsAnyAsync<Exception>(() => ConnectWithOriginAsync(factory, "https://evil.example"));
    }

    [Fact]
    public async Task WebSocket_FromAnAllowedOrigin_Connects()
    {
        await using var factory = WithOptions(security: o => o.AllowedOrigins = ["https://app.example"]);

        using var socket = await ConnectWithOriginAsync(factory, "https://app.example");

        Assert.NotEqual(Guid.Empty, await GatewayTestHelpers.CreateMatrixSessionAsync(socket));
    }

    [Fact]
    public async Task WebSocket_WithoutAnOrigin_IsNotChecked()
    {
        await using var factory = WithOptions(security: o => o.AllowedOrigins = ["https://app.example"]);

        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        Assert.NotEqual(Guid.Empty, await GatewayTestHelpers.CreateMatrixSessionAsync(socket));
    }

    [Fact]
    public async Task Cors_AllowsTheListedOriginOnly()
    {
        await using var factory = WithOptions(security: o => o.AllowedOrigins = ["https://app.example"]);
        var client = factory.CreateClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/operations");
        allowed.Headers.Add("Origin", "https://app.example");
        var allowedResponse = await client.SendAsync(allowed);

        using var refused = new HttpRequestMessage(HttpMethod.Get, "/operations");
        refused.Headers.Add("Origin", "https://evil.example");
        var refusedResponse = await client.SendAsync(refused);

        Assert.Equal("https://app.example", Assert.Single(allowedResponse.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(refusedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ---------- connections per address ----------

    [Fact]
    public async Task WebSocket_BeyondThePerAddressLimit_IsRefused_UntilAnotherCloses()
    {
        await using var factory = WithOptions(limits: o => o.MaxConnectionsPerIp = 2);
        using var first = await GatewayTestHelpers.ConnectAsync(factory);
        using var second = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.CreateMatrixSessionAsync(first);
        await GatewayTestHelpers.CreateMatrixSessionAsync(second);

        await Assert.ThrowsAnyAsync<Exception>(() => GatewayTestHelpers.ConnectAsync(factory));

        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        WebSocket? replacement = null;
        for (int attempt = 0; attempt < 50 && replacement is null; attempt++)
        {
            try { replacement = await GatewayTestHelpers.ConnectAsync(factory); }
            catch (Exception) { await Task.Delay(100); } // the server frees the slot a moment after the close
        }
        Assert.NotNull(replacement);
        replacement!.Dispose();
    }

    // ---------- repeated request ids ----------

    [Fact]
    public async Task ExecuteWithAnAlreadyUsedRequestId_IsNotRunTwice()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.SendAsync(socket, new CreateSessionMessage("tree", Json(new { })));
        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        var requestId = Guid.NewGuid();
        var insert = new ExecuteOperationMessage(requestId, "insert-node", new() { ["value"] = Json(5) });

        await GatewayTestHelpers.SendAsync(socket, insert);
        ServerMessage reply;
        do { reply = await GatewayTestHelpers.ReceiveAsync(socket); } while (reply is StepEventMessage);
        Assert.IsType<OperationCompleteMessage>(reply);

        await GatewayTestHelpers.SendAsync(socket, insert);
        var refused = Assert.IsType<OperationFailedMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal(requestId, refused.RequestId);
        Assert.StartsWith("duplicate-request", refused.Reason);

        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(Guid.NewGuid()));
        var state = Assert.IsType<StateMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal(1, state.State.Payload.GetProperty("nodes").GetArrayLength());
    }
}
