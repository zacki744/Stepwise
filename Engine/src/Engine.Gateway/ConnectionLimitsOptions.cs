namespace Engine.Gateway;

public sealed class ConnectionLimitsOptions
{
    /// <summary>Largest single client message accepted. 1 MiB is roughly 50k graph edges of JSON.</summary>
    public int MaxMessageBytes { get; set; } = 1024 * 1024;
}