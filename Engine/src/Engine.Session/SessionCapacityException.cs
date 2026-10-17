namespace Engine.Session;

public sealed class SessionCapacityException : Exception
{
    public SessionCapacityException(int maxSessions)
        : base($"Server is at its limit of {maxSessions} concurrent sessions; try again shortly")
        => MaxSessions = maxSessions;

    /// <summary>The server-wide dataset memory budget is full (the session count itself is fine).</summary>
    public SessionCapacityException(string message) : base(message)
    {
    }

    public int MaxSessions { get; }
}