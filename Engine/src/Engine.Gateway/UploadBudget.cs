namespace Engine.Gateway;

/// <summary>
/// Shared ceiling on how many bytes of half-received uploads the server holds across all connections,
/// so many clients uploading at once cannot exhaust memory. Reservations are made as chunks arrive and
/// returned when an upload finishes, fails or its connection ends.
/// </summary>
public sealed class UploadBudget(long maxBytes)
{
    private long _inFlight;

    public long InFlightBytes => Volatile.Read(ref _inFlight);

    public bool TryReserve(long bytes)
    {
        while (true)
        {
            long current = Volatile.Read(ref _inFlight);
            long next = current + bytes;
            if (next > maxBytes) return false;
            if (Interlocked.CompareExchange(ref _inFlight, next, current) == current) return true;
        }
    }

    public void Release(long bytes) => Interlocked.Add(ref _inFlight, -bytes);
}
