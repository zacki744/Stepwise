using System.Text.Json;

namespace Engine.Gateway;

/// <summary>
/// One valid options object per operation, including every optional key, read from <see cref="OperationDefinitions"/>.
/// Doubles as documentation (served by GET /operations) and as the input for the contract tests. The sample
/// datasets those tests use: a 6x8 matrix, an 8-node weighted undirected graph, and the tree 50,30,70,20,40,60,80.
/// </summary>
public static class OperationExamples
{
    private static readonly Dictionary<string, string> Examples =
        OperationDefinitions.All.ToDictionary(d => d.Descriptor.Id, d => d.ExampleJson);

    public static IEnumerable<string> OperationIds => Examples.Keys;

    public static JsonElement? TryGetElement(string operationId)
    {
        if (!Examples.TryGetValue(operationId, out var text)) return null;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>The example as an options dictionary, ready to hand to a handler.</summary>
    public static Dictionary<string, JsonElement> Parse(string operationId)
    {
        using var document = JsonDocument.Parse(Examples[operationId]);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
