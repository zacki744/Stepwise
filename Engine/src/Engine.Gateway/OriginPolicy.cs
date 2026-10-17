namespace Engine.Gateway;

public static class OriginPolicy
{
    /// <summary>
    /// No Origin header means the caller is not a browser page, and an empty allow-list means the check is
    /// switched off. Otherwise the origin must match an entry exactly (scheme, host and port; case and a
    /// trailing slash ignored). Browsers send the literal "null" from sandboxed frames and file:// pages;
    /// that never matches, so it is refused once a list is set.
    /// </summary>
    public static bool IsAllowed(string? origin, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrEmpty(origin) || allowed.Count == 0) return true;
        var normalized = origin.TrimEnd('/');
        return allowed.Any(a => string.Equals(a.TrimEnd('/'), normalized, StringComparison.OrdinalIgnoreCase));
    }
}
