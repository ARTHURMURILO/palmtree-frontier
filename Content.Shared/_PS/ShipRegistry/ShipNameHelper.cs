using System;

namespace Content.Shared._PS.ShipRegistry;

/// <summary>
/// Palmtree: shared helpers for the consoles that name ships. A shipyard-style name is a base name
/// plus a short registration suffix ("Dove" + "QT-723"), and either side of that split can end up
/// carrying the other one: renames append a suffix, and the dock used to store the full name as the
/// deed's base name, so repeated store/retrieve cycles grew it ("Dove NX-674 NX-647").
/// </summary>
public static class ShipNameHelper
{
    /// <summary>
    /// Builds a full name without appending the suffix twice when the name already ends with it.
    /// </summary>
    public static string BuildFullName(string name, string? suffix)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(suffix))
            return name;

        suffix = suffix.Trim();
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return name;

        return $"{name} {suffix}";
    }

    /// <summary>
    /// The base name a deed should carry: strips a trailing registration suffix from a full name so
    /// it cannot accumulate ("Dove NX-674" + "NX-647" -> "Dove").
    /// </summary>
    public static string GetBaseName(string name, string? suffix)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(suffix))
            return name;

        suffix = suffix.Trim();
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return name;

        return name[..^suffix.Length].TrimEnd();
    }
}
