using System.Text.Json;
using Engine.Domain.Operations;
using Engine.Session;

namespace Engine.Gateway;

/// <summary>The client sent options an operation cannot accept. Always the client's fault, never a server bug.</summary>
public sealed class InvalidOptionsException(string message) : ArgumentException(message);

/// <summary>
/// Checks a request's options against the operation's descriptor before anything is built from them:
/// unknown names (a typo such as "quarterTurn" must not silently run with the default), missing required
/// options, and values of the wrong JSON type or outside the listed choices. After this passes, the handler's
/// own reading of the options cannot fail on shape, so a failure there is a real bug and not "invalid options".
/// Domain rules (a node outside the graph, a negative size) are still enforced by the operations themselves.
/// </summary>
public static class OptionValidator
{
    public static void Validate(OperationDescriptor descriptor, IReadOnlyDictionary<string, JsonElement> options)
    {
        foreach (var name in options.Keys)
        {
            if (descriptor.Options.Any(o => o.Name == name)) continue;
            throw new InvalidOptionsException(descriptor.Options.Count == 0
                ? $"'{descriptor.Id}' takes no options, but '{name}' was given"
                : $"Unknown option '{name}' for '{descriptor.Id}'. Known options: {string.Join(", ", descriptor.Options.Select(o => o.Name))}");
        }

        foreach (var option in descriptor.Options)
        {
            if (!options.TryGetValue(option.Name, out var value))
            {
                if (option.Required) throw new InvalidOptionsException($"Missing required option '{option.Name}' for '{descriptor.Id}'");
                continue;
            }

            var problem = Describe(option, value);
            if (problem is not null) throw new InvalidOptionsException($"Option '{option.Name}' {problem}");
        }
    }

    // Null means "the value is fine"; otherwise what is wrong, phrased to follow "Option 'x' ".
    private static string? Describe(OptionDescriptor option, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return "must not be null (leave it out to use the default)";

        switch (option.Kind)
        {
            case OptionKinds.Int or OptionKinds.Row or OptionKinds.Column or OptionKinds.Node or OptionKinds.Seed:
                return IsInteger(value) ? null : "must be a whole number";

            case OptionKinds.Number:
                return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && double.IsFinite(d)
                    ? null : "must be a number";

            case OptionKinds.Bool:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "must be true or false";

            case OptionKinds.Choice:
                return value.ValueKind == JsonValueKind.String && option.Choices!.Contains(value.GetString()!)
                    ? null : $"must be one of: {string.Join(", ", option.Choices!)}";

            case OptionKinds.NodeList:
                return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(IsInteger)
                    ? null : "must be a list of whole numbers";

            case OptionKinds.ItemList:
                return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(IsItem)
                    ? null : "must be a list of objects like {\"id\":1,\"width\":2,\"height\":3} with whole numbers";

            default:
                throw new InvalidOperationException($"Unknown option kind '{option.Kind}'");
        }
    }

    private static bool IsInteger(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _);

    private static bool IsItem(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object
        && new[] { "id", "width", "height" }.All(key => item.TryGetProperty(key, out var v) && IsInteger(v));
}

/// <summary>Wraps a handler so its options are validated against the descriptor on every path that runs it (live, rehydration replay, tests).</summary>
public sealed class ValidatingOperationHandler(IOperationHandler inner, OperationDescriptor descriptor) : IOperationHandler
{
    public string OperationId => inner.OperationId;
    public string DatasetType => inner.DatasetType;

    public OperationOutcome Execute(
        SessionManager manager, Guid sessionId,
        IReadOnlyDictionary<string, JsonElement> options, OperationContext context)
    {
        OptionValidator.Validate(descriptor, options);
        return inner.Execute(manager, sessionId, options, context);
    }
}
