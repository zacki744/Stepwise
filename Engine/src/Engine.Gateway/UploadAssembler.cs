using System.Text;
using Engine.Protocol;

namespace Engine.Gateway;

public enum UploadStatus { InProgress, Complete, Rejected }

public sealed record UploadResult(UploadStatus Status, string? Json = null, string? Code = null, string? Message = null);

/// <summary>
/// Reassembles one connection's chunked upload. Pure state machine (no sockets), so every rule is unit
/// testable. At most one upload per connection: chunk 0 always starts a fresh one and discards any
/// partial upload. Chunks must arrive in order (a WebSocket already guarantees that, so a gap means a
/// client bug), and any violation discards the partial upload so nothing half-built lingers.
/// After a Complete result the buffered bytes stay reserved until <see cref="Reset"/>, so the memory
/// used while the caller parses and handles the message is still counted against the shared budget.
/// </summary>
public sealed class UploadAssembler(ConnectionLimitsOptions limits, UploadBudget budget) : IDisposable
{
    private StringBuilder? _text;
    private Guid _uploadId;
    private int _total;
    private int _next;
    private long _bytes;
    private DateTime _startedUtc;

    public bool InProgress => _text is not null;

    /// <summary>How long until the current upload must be finished, or null when none is running.</summary>
    public TimeSpan? TimeRemaining
    {
        get
        {
            if (_text is null) return null;
            var left = _startedUtc + limits.UploadTimeout - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public UploadResult Accept(UploadChunkMessage chunk)
    {
        if (chunk.Data is null)
            return Reject("invalid-upload", "An upload chunk needs its data");
        if (chunk.Total < 1 || chunk.Total > limits.MaxUploadChunks)
            return Reject("invalid-upload", $"total must be between 1 and {limits.MaxUploadChunks:N0} chunks (got {chunk.Total})");

        if (chunk.Index == 0)
        {
            Reset();
            _text = new StringBuilder();
            _uploadId = chunk.UploadId;
            _total = chunk.Total;
            _startedUtc = DateTime.UtcNow;
        }
        else if (_text is null || chunk.UploadId != _uploadId || chunk.Total != _total || chunk.Index != _next)
        {
            return Reject("upload-out-of-order",
                $"Expected chunk {(_text is null ? "0 of a new upload" : _next.ToString())}, got index {chunk.Index} of upload {chunk.UploadId}");
        }

        long size = Encoding.UTF8.GetByteCount(chunk.Data);
        if (_bytes + size > limits.MaxUploadBytes)
            return Reject("upload-too-large", $"An upload may not exceed {limits.MaxUploadBytes:N0} bytes");
        if (!budget.TryReserve(size))
            return Reject("server-busy", "The server is holding its maximum amount of upload data; try again shortly");

        var text = _text!;
        _bytes += size;
        text.Append(chunk.Data);
        _next++;

        return _next < _total
            ? new UploadResult(UploadStatus.InProgress)
            : new UploadResult(UploadStatus.Complete, Json: text.ToString());
    }

    /// <summary>Discards any upload in progress and returns its reserved bytes.</summary>
    public void Reset()
    {
        if (_bytes > 0) budget.Release(_bytes);
        _text = null;
        _bytes = 0;
        _next = 0;
        _total = 0;
    }

    public void Dispose() => Reset();

    private UploadResult Reject(string code, string message)
    {
        Reset();
        return new UploadResult(UploadStatus.Rejected, Code: code, Message: message);
    }
}
