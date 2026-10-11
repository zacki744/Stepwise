namespace Engine.Session;

public sealed class SessionCapacityException(int maxSessions)
    : Exception($"Server is at its limit of {maxSessions} concurrent sessions; try again shortly")
{
    public int MaxSessions { get; } = maxSessions;
}