using System.Net.WebSockets;
using System.Text;

namespace Engine.Gateway;

public sealed class MessageTooLargeException(int maxBytes)
    : Exception($"Message exceeds the {maxBytes:N0}-byte limit")
{
    public int MaxBytes { get; } = maxBytes;
}

public sealed class MessageTimeoutException(TimeSpan timeout)
    : Exception($"A message that was started was not completed within {timeout.TotalSeconds:0.#} seconds")
{
    public TimeSpan Timeout { get; } = timeout;
}

public static class BoundedMessageReader
{
    /// <summary>
    /// Reads one complete text message, or null if the peer closed. Checks the limit as each
    /// frame arrives, so an endless fragmented message is rejected promptly instead of being
    /// buffered until memory runs out. Waiting for a message to START is bounded only by <paramref name="ct"/>
    /// (the caller's idle rule); once its first frame has arrived, the rest must follow within
    /// <paramref name="messageTimeout"/>, so a client cannot trickle a message in forever. From then on the idle
    /// rule no longer applies, only that clock and <paramref name="shutdown"/> (the connection or server going away).
    /// </summary>
    public static async Task<string?> ReadAsync(
        WebSocket socket, int maxBytes, CancellationToken ct, TimeSpan? messageTimeout = null, CancellationToken shutdown = default)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        using var messageClock = messageTimeout is { } t && t > TimeSpan.Zero ? CancellationTokenSource.CreateLinkedTokenSource(shutdown) : null;
        var readToken = ct;
        WebSocketReceiveResult result;
        do
        {
            try
            {
                result = await socket.ReceiveAsync(buffer, readToken);
            }
            catch (OperationCanceledException) when (messageClock is not null && messageClock.IsCancellationRequested && !shutdown.IsCancellationRequested)
            {
                throw new MessageTimeoutException(messageTimeout!.Value);
            }

            if (messageClock is not null && readToken == ct && !result.EndOfMessage)
            {
                messageClock.CancelAfter(messageTimeout!.Value); // the message has begun: the clock starts now
                readToken = messageClock.Token;
            }
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Echo the close frame so the client's CloseAsync can complete normally.
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ack", ct);
                return null;
            }

            if (stream.Length + result.Count > maxBytes)
                throw new MessageTooLargeException(maxBytes);

            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }
}