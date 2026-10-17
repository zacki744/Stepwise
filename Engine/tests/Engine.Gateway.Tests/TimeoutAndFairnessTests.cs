using System.Net.WebSockets;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Engine.Gateway.Tests;

public class TimeoutAndFairnessTests
{
    // ---------- BoundedMessageReader: message completion ----------

    [Fact]
    public async Task Reader_MessageThatStallsAfterItsFirstFrame_TimesOut()
    {
        var socket = new StallingWebSocket(firstFrameBytes: 10);

        await Assert.ThrowsAsync<MessageTimeoutException>(() => BoundedMessageReader.ReadAsync(
            socket, maxBytes: 1_000, CancellationToken.None, TimeSpan.FromMilliseconds(100), CancellationToken.None));
    }

    [Fact]
    public async Task Reader_WaitingForAMessageToStart_IsNotSubjectToTheMessageTimeout()
    {
        var socket = new StallingWebSocket(firstFrameBytes: 0); // nothing ever arrives
        using var idle = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // The message clock is 50 ms; it must not fire while no message has begun. Only the idle token ends the wait.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BoundedMessageReader.ReadAsync(
            socket, maxBytes: 1_000, idle.Token, TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task Reader_ShutdownDuringAMessage_IsACancellation_NotATimeout()
    {
        var socket = new StallingWebSocket(firstFrameBytes: 10);
        using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BoundedMessageReader.ReadAsync(
            socket, maxBytes: 1_000, shutdown.Token, TimeSpan.FromSeconds(30), shutdown.Token));
    }

    // ---------- ExecutionPolicy: per-client share ----------

    private static ExecutionPolicy Policy(int slots, int perClient, int queueMs = 50) =>
        new(Options.Create(new ExecutionLimitsOptions
        {
            MaxConcurrentOperations = slots,
            MaxConcurrentOperationsPerClient = perClient,
            QueueTimeout = TimeSpan.FromMilliseconds(queueMs)
        }));

    [Fact]
    public async Task Policy_ClientBeyondItsShare_IsRefused_WhileOthersStillGetTheFreeSlot()
    {
        using var policy = Policy(slots: 3, perClient: 2);
        using var a1 = await policy.TryEnterAsync("a", CancellationToken.None);
        using var a2 = await policy.TryEnterAsync("a", CancellationToken.None);

        var a3 = await policy.TryEnterAsync("a", CancellationToken.None); // a slot is free, but it is not a's to take
        using var b1 = await policy.TryEnterAsync("b", CancellationToken.None);

        Assert.NotNull(a1);
        Assert.NotNull(a2);
        Assert.Null(a3);
        Assert.NotNull(b1);
    }

    [Fact]
    public async Task Policy_DisposingALease_GivesTheClientItsShareBack()
    {
        using var policy = Policy(slots: 4, perClient: 1);
        var first = await policy.TryEnterAsync("a", CancellationToken.None);
        Assert.Null(await policy.TryEnterAsync("a", CancellationToken.None));

        first!.Dispose();
        first.Dispose(); // idempotent: must not hand out a second share

        using var again = await policy.TryEnterAsync("a", CancellationToken.None);
        Assert.NotNull(again);
        Assert.Null(await policy.TryEnterAsync("a", CancellationToken.None));
    }

    [Fact]
    public async Task Policy_TimingOutInTheQueue_ReleasesTheClientCount()
    {
        using var policy = Policy(slots: 1, perClient: 5);
        var holder = await policy.TryEnterAsync("a", CancellationToken.None);

        Assert.Null(await policy.TryEnterAsync("b", CancellationToken.None)); // waits 50 ms, then gives up
        holder!.Dispose();

        using var b = await policy.TryEnterAsync("b", CancellationToken.None);
        Assert.NotNull(b);
    }

    [Fact]
    public async Task Policy_CancelledWhileQueued_ReleasesTheClientCount()
    {
        using var policy = Policy(slots: 1, perClient: 1, queueMs: 10_000);
        var holder = await policy.TryEnterAsync("a", CancellationToken.None);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.TryEnterAsync("b", cancel.Token));
        holder!.Dispose();

        using var b = await policy.TryEnterAsync("b", CancellationToken.None);
        Assert.NotNull(b);
    }

    [Fact]
    public async Task Policy_ZeroShare_MeansNoPerClientLimit()
    {
        using var policy = Policy(slots: 3, perClient: 0);
        using var one = await policy.TryEnterAsync("a", CancellationToken.None);
        using var two = await policy.TryEnterAsync("a", CancellationToken.None);
        using var three = await policy.TryEnterAsync("a", CancellationToken.None);

        Assert.NotNull(three);
    }

    // ---------- endpoint: silent and stalled connections ----------

    private static WebApplicationFactory<Program> WithLimits(Action<ConnectionLimitsOptions> configure) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure(configure)));

    // The server must end the connection: a close frame, or an abrupt end the client sees as an exception.
    private static async Task AssertServerEndsConnectionAsync(WebSocket socket)
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var buffer = new byte[256];
            WebSocketReceiveResult result;
            do { result = await socket.ReceiveAsync(buffer, guard.Token); }
            while (result.MessageType != WebSocketMessageType.Close);
        }
        catch (WebSocketException)
        {
            // Ended without a close handshake: still ended.
        }
        Assert.False(guard.IsCancellationRequested, "the server never ended the connection");
    }

    [Fact]
    public async Task ConnectionWithNoSession_ThatStaysSilent_IsClosed()
    {
        await using var factory = WithLimits(o => o.PreSessionTimeout = TimeSpan.FromMilliseconds(300));
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await AssertServerEndsConnectionAsync(socket);
    }

    [Fact]
    public async Task ConnectionWithASession_IsNotClosedByThePreSessionTimeout()
    {
        await using var factory = WithLimits(o => o.PreSessionTimeout = TimeSpan.FromMilliseconds(300));
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.CreateMatrixSessionAsync(socket);

        await Task.Delay(700);

        // Still alive: a request is answered.
        await GatewayTestHelpers.SendCreateMatrixAsync(socket, 2, 2);
        Assert.IsType<Engine.Protocol.SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
    }

    [Fact]
    public async Task ConnectionWithASession_ThatStaysSilent_IsClosedAfterTheIdleTimeout()
    {
        await using var factory = WithLimits(o => o.IdleTimeout = TimeSpan.FromMilliseconds(300));
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.CreateMatrixSessionAsync(socket);

        await AssertServerEndsConnectionAsync(socket);
    }

    [Fact]
    public async Task MessageThatIsStartedButNeverFinished_IsClosed()
    {
        await using var factory = WithLimits(o => o.MessageTimeout = TimeSpan.FromMilliseconds(300));
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        // Half a message: the final frame never comes.
        await socket.SendAsync("{\"type\":\"createSe"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);

        await AssertServerEndsConnectionAsync(socket);
    }

    // Returns one frame (or none), then waits forever, honouring cancellation.
    private sealed class StallingWebSocket(int firstFrameBytes) : WebSocket
    {
        private bool _gaveFirst;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (firstFrameBytes > 0 && !_gaveFirst)
            {
                _gaveFirst = true;
                Array.Fill(buffer.Array!, (byte)'a', buffer.Offset, firstFrameBytes);
                return new WebSocketReceiveResult(firstFrameBytes, WebSocketMessageType.Text, endOfMessage: false);
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
