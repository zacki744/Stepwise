using System.Text.Json;

namespace Engine.Protocol;

/// <summary>
/// The single shared serialization contract for everything in this namespace.
/// Every place that serializes or deserializes a ClientMessage/ServerMessage
/// must use these options — a mismatch here is exactly what caused
/// datasetType to silently bind to null instead of throwing a clear error.
/// </summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}