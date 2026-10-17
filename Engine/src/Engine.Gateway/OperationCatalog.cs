using Engine.Session;

namespace Engine.Gateway;

/// <summary>
/// The registry of every operation the server can run. It holds no list of its own: operations are defined,
/// one entry each, in the <c>Definitions</c> folder (see <see cref="OperationDefinitions"/>), and this just
/// registers them. Registration stays explicit, with no reflection, so the full list is visible and navigable.
/// </summary>
public static class OperationCatalog
{
    /// <summary>
    /// The registry the server runs: every handler checks its options against its descriptor first, so
    /// unknown, missing or wrongly typed options are refused with a clear message.
    /// </summary>
    public static OperationRegistry BuildDefault()
    {
        var validated = new OperationRegistry();
        foreach (var handler in BuildUnvalidated().All)
        {
            var descriptor = OperationDescriptors.TryGet(handler.OperationId)
                ?? throw new InvalidOperationException($"Operation '{handler.OperationId}' has no descriptor, so its options cannot be validated");
            validated.Register(new ValidatingOperationHandler(handler, descriptor));
        }
        return validated;
    }

    /// <summary>The bare handlers, exactly as defined. For tests that check the descriptors against what the handlers themselves demand.</summary>
    public static OperationRegistry BuildUnvalidated()
    {
        var registry = new OperationRegistry();
        foreach (var definition in OperationDefinitions.All) registry.Register(definition.Handler);
        return registry;
    }
}
