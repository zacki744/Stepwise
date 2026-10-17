using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Gateway;

/// <summary>
/// The JSON shape of a value, derived from a C# type by reflection so it cannot drift from what the engine
/// really sends. <c>Type</c> is one of int, number, bool, text, list, map, object, any. Objects list their
/// <c>Fields</c> (camelCase, as on the wire); lists and maps describe their elements in <c>Item</c>.
/// </summary>
public sealed record Shape(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    string Type,
    bool Nullable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Shape>? Fields,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Shape? Item);

public static class ShapeReader
{
    private const int MaxDepth = 8;

    public static Shape Of(Type type, bool nullable = false)
        => Read(type, nullable, name: null, info: null, new NullabilityInfoContext(), depth: 0);

    /// <summary>The fields of a record, in declaration order. Used for step payloads.</summary>
    public static IReadOnlyList<Shape> FieldsOf(Type recordType)
        => Read(recordType, false, null, null, new NullabilityInfoContext(), 0).Fields
           ?? throw new InvalidOperationException($"{recordType.Name} is not an object type");

    private static Shape Read(Type type, bool nullable, string? name, NullabilityInfo? info, NullabilityInfoContext context, int depth)
    {
        if (depth > MaxDepth) throw new InvalidOperationException($"{type.Name} is nested too deeply to describe");

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) { type = underlying; nullable = true; }
        if (info?.ReadState == NullabilityState.Nullable) nullable = true;

        if (type == typeof(bool)) return new(name, "bool", nullable, null, null);
        if (type == typeof(string) || type.IsEnum) return new(name, "text", nullable, null, null);
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return new(name, "number", nullable, null, null);
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort))
            return new(name, "int", nullable, null, null);
        if (type == typeof(object)) return new(name, "any", nullable, null, null);

        var dictionary = GenericArguments(type, typeof(IReadOnlyDictionary<,>)) ?? GenericArguments(type, typeof(IDictionary<,>));
        if (dictionary is not null)
            return new(name, "map", nullable, null,
                Read(dictionary[1], false, null, info?.GenericTypeArguments.ElementAtOrDefault(1), context, depth + 1));

        if (type.IsArray)
            return new(name, "list", nullable, null, Read(type.GetElementType()!, false, null, info?.ElementType, context, depth + 1));
        var sequence = GenericArguments(type, typeof(IEnumerable<>));
        if (sequence is not null)
            return new(name, "list", nullable, null,
                Read(sequence[0], false, null, info?.GenericTypeArguments.ElementAtOrDefault(0), context, depth + 1));

        var fields = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => Read(p.PropertyType, false, JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                context.Create(p), context, depth + 1))
            .ToList();
        return new(name, "object", nullable, fields, null);
    }

    private static Type[]? GenericArguments(Type type, Type definition)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == definition) return type.GetGenericArguments();
        var match = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == definition);
        return match?.GetGenericArguments();
    }
}

/// <summary>What an operation hands back as its value, and the shape of that value.</summary>
public sealed record ReturnsDescriptor(string Description, Shape Value);

/// <summary>
/// How a UI should draw one kind of step. <c>Kind</c> comes from <see cref="StepVisuals"/>; <c>Bind</c> maps
/// each role the visual needs (node, from, to, row, column, slot, nodes, path) to the payload field that holds it.
/// </summary>
public sealed record VisualHint(string Kind, IReadOnlyDictionary<string, string> Bind);

public static class StepVisuals
{
    public const string None = "none";
    public const string HighlightNode = "highlightNode";
    public const string HighlightNodes = "highlightNodes";
    public const string HighlightPath = "highlightPath";
    public const string HighlightEdge = "highlightEdge";
    public const string HighlightCell = "highlightCell";
    public const string HighlightSlot = "highlightSlot";   // a position in a tree

    public static readonly IReadOnlyList<string> All =
        [None, HighlightNode, HighlightNodes, HighlightPath, HighlightEdge, HighlightCell, HighlightSlot];

    public static VisualHint Nothing { get; } = new(None, new Dictionary<string, string>());
    private static string Wire(string field) => JsonNamingPolicy.CamelCase.ConvertName(field);

    public static VisualHint Node(string field) => new(HighlightNode, new Dictionary<string, string> { ["node"] = Wire(field) });
    public static VisualHint Nodes(string field) => new(HighlightNodes, new Dictionary<string, string> { ["nodes"] = Wire(field) });
    public static VisualHint Route(string field) => new(HighlightPath, new Dictionary<string, string> { ["path"] = Wire(field) });
    public static VisualHint Slot(string field) => new(HighlightSlot, new Dictionary<string, string> { ["slot"] = Wire(field) });
    public static VisualHint Edge(string from, string to) =>
        new(HighlightEdge, new Dictionary<string, string> { ["from"] = Wire(from), ["to"] = Wire(to) });
    public static VisualHint Cell(string row, string column) =>
        new(HighlightCell, new Dictionary<string, string> { ["row"] = Wire(row), ["column"] = Wire(column) });
}

/// <summary>One kind of step an operation can emit: its type name, what it means, what it carries, how to draw it.</summary>
public sealed record StepDescriptor(string Type, string Description, VisualHint Visual, IReadOnlyList<Shape> Payload)
{
    /// <summary>The payload type the engine really attaches (null for steps without one). Not sent to clients.</summary>
    [JsonIgnore] public Type? PayloadType { get; init; }
}

/// <summary>What an operation returns and which steps it narrates. Effect is derived separately from the operation itself.</summary>
public sealed record OperationOutput(ReturnsDescriptor? Returns, IReadOnlyList<StepDescriptor> Steps);
