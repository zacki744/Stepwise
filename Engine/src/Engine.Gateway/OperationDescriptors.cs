namespace Engine.Gateway;

/// <summary>
/// What a client needs to build a form for an operation without hard-coding it: a label, a category
/// to group by, a description, and each option's semantic kind. The "kind" says what the value MEANS
/// (a node index, a grid row, a random seed), not just its JSON type, so a UI can offer node pickers
/// or cell pickers instead of bare number boxes.
/// </summary>
public sealed record OptionDescriptor(
    string Name,
    string Kind,
    string Label,
    bool Required,
    object? Default = null,
    IReadOnlyList<string>? Choices = null);

public sealed record OperationDescriptor(
    string Id,
    string Label,
    string Category,
    string Description,
    IReadOnlyList<OptionDescriptor> Options);

public static class OptionKinds
{
    public const string Int = "int";
    public const string Number = "number";
    public const string Bool = "bool";
    public const string Choice = "choice";
    public const string Row = "row";
    public const string Column = "column";
    public const string Node = "node";
    public const string NodeList = "nodeList";
    public const string Seed = "seed";
    public const string ItemList = "itemList";
}

/// <summary>
/// Client-facing metadata for every registered operation, read from <see cref="OperationDefinitions"/>.
/// OperationDescriptorTests still checks it against the real handlers (declared required flags against what
/// the handlers demand, defaults and examples against each option's kind).
/// </summary>
public static class OperationDescriptors
{
    private static readonly Dictionary<string, OperationDescriptor> ById =
        OperationDefinitions.All.ToDictionary(d => d.Descriptor.Id, d => d.Descriptor);

    public static IEnumerable<OperationDescriptor> All => ById.Values;

    public static OperationDescriptor? TryGet(string operationId) => ById.GetValueOrDefault(operationId);
}
